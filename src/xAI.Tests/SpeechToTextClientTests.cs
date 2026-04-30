using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Grpc.Net.Client;
using Microsoft.Extensions.AI;

namespace xAI.Tests;

public class SpeechToTextClientTests
{
    [Fact]
    public void AsISpeechToTextClient_ReturnsMetadata()
    {
        using var client = new GrokClient("test-api-key", CreateOptions(new CaptureHandler()));
        using var stt = client.AsISpeechToTextClient();

        var metadata = stt.GetService<SpeechToTextClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("xai", metadata.ProviderName);
        Assert.Equal(client.Options.Endpoint, metadata.ProviderUri);
        Assert.Null(metadata.DefaultModelId);
    }

    [Fact]
    public async Task GetTextAsync_MapsRequestAndResponse()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "text": "Hello world",
                  "language": "English",
                  "duration": 1.25,
                  "words": [
                    { "text": "Hello", "start": 0.10, "end": 0.50 },
                    { "text": "world", "start": 0.60, "end": 1.10 }
                  ]
                }
                """, Encoding.UTF8, "application/json"),
        });

        using var client = new GrokClient("test-api-key", CreateOptions(handler));
        using var stt = client.AsISpeechToTextClient();

        var response = await stt.GetTextAsync(new MemoryStream([1, 2, 3]),
            new GrokSpeechToTextOptions
            {
                TextLanguage = "en",
                SpeechSampleRate = 16000,
                Format = true,
                AudioFormat = "pcm",
                Multichannel = true,
                Channels = 2,
                Diarize = true,
                ModelId = "test-model",
            });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(new Uri($"{client.Options.Endpoint}v1/stt"), handler.Request.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("test-api-key", handler.Request.Headers.Authorization?.Parameter);

        var body = handler.RequestBody!;
        AssertFieldOrder(body, "format", "language", "sample_rate", "audio_format", "multichannel", "channels", "diarize", "file");
        Assert.Contains("format", GetField(body, "format"));
        Assert.Contains("true", body);
        Assert.Contains("language", GetField(body, "language"));
        Assert.Contains("en", body);
        Assert.Contains("sample_rate", GetField(body, "sample_rate"));
        Assert.Contains("16000", body);
        Assert.Contains("audio_format", GetField(body, "audio_format"));
        Assert.Contains("pcm", body);
        Assert.Contains("audio.mp3", body);

        Assert.Equal("Hello world", response.Text);
        Assert.Null(response.ModelId);
        Assert.Equal(TimeSpan.FromSeconds(0.10), response.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(1.10), response.EndTime);
        Assert.Equal("English", response.AdditionalProperties?["language"]);
        Assert.Equal(1.25, response.AdditionalProperties?["duration"]);
    }

    [Fact]
    public async Task GetTextAsync_WithError_ThrowsHttpRequestException()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent("""{"error":"missing file"}"""),
        });

        using var client = new GrokClient("test-api-key", CreateOptions(handler));
        using var stt = client.AsISpeechToTextClient();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => stt.GetTextAsync(new MemoryStream([1])));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("missing file", exception.Message);
    }

    [Fact]
    public async Task GetTextAsync_WithNullStream_ThrowsArgumentNullException()
    {
        using var client = new GrokClient("test-api-key", CreateOptions(new CaptureHandler()));
        using var stt = client.AsISpeechToTextClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => stt.GetTextAsync(null!));
    }

    [Fact]
    public async Task GetTextAsync_WithTranslation_ThrowsNotSupportedException()
    {
        using var client = new GrokClient("test-api-key", CreateOptions(new CaptureHandler()));
        using var stt = client.AsISpeechToTextClient();

        await Assert.ThrowsAsync<NotSupportedException>(() => stt.GetTextAsync(new MemoryStream([1]),
            new SpeechToTextOptions
            {
                SpeechLanguage = "en",
                TextLanguage = "fr",
            }));
    }

    [Fact]
    public async Task GetTextAsync_WithFormatAndNoLanguage_ThrowsArgumentException()
    {
        using var client = new GrokClient("test-api-key", CreateOptions(new CaptureHandler()));
        using var stt = client.AsISpeechToTextClient();

        await Assert.ThrowsAsync<ArgumentException>(() => stt.GetTextAsync(new MemoryStream([1]),
            new GrokSpeechToTextOptions { Format = true }));
    }

    [Fact]
    public async Task GetStreamingTextAsync_MapsWebSocketEvents()
    {
        var webSocket = new FakeWebSocket(
            """{"type":"transcript.created"}""",
            """{"type":"transcript.partial","text":"Hel","is_final":false,"speech_final":false,"start":0.0,"duration":0.4}""",
            """{"type":"transcript.partial","text":"Hello","is_final":true,"speech_final":true,"start":0.0,"duration":0.8,"channel_index":1}""",
            """{"type":"transcript.done","text":"Hello world","duration":1.2}""");

        Uri? capturedUri = null;
        string? capturedApiKey = null;
        using var stt = new GrokSpeechToTextClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://streaming.test/base/"),
            "test-api-key",
            (uri, apiKey, _) =>
            {
                capturedUri = uri;
                capturedApiKey = apiKey;
                return ValueTask.FromResult<WebSocket>(webSocket);
            });

        var updates = new List<SpeechToTextResponseUpdate>();
        await foreach (var update in stt.GetStreamingTextAsync(new MemoryStream([1, 2, 3, 4]),
            new GrokSpeechToTextOptions
            {
                AudioFormat = "mulaw",
                SpeechSampleRate = 8000,
                TextLanguage = "en",
                InterimResults = true,
                Endpointing = 5,
                Diarize = true,
                Multichannel = true,
                Channels = 2,
                ModelId = "ignored-model",
            }))
        {
            updates.Add(update);
        }

        Assert.Equal("test-api-key", capturedApiKey);
        Assert.Equal("wss://streaming.test/base/v1/stt?sample_rate=8000&encoding=mulaw&interim_results=true&endpointing=5&language=en&diarize=true&multichannel=true&channels=2", capturedUri!.ToString());

        Assert.Collection(webSocket.SentBinaryMessages,
            message => Assert.Equal(new byte[] { 1, 2, 3, 4 }, message));

        Assert.Collection(webSocket.SentTextMessages,
            message =>
            {
                using var json = JsonDocument.Parse(message);
                Assert.Equal("audio.done", json.RootElement.GetProperty("type").GetString());
            });

        Assert.Collection(updates,
            update =>
            {
                Assert.Equal(SpeechToTextResponseUpdateKind.SessionOpen, update.Kind);
                Assert.Null(update.ModelId);
            },
            update =>
            {
                Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdating, update.Kind);
                Assert.Null(update.ModelId);
                Assert.Equal("Hel", update.Text);
                Assert.Equal(TimeSpan.Zero, update.StartTime);
                Assert.Equal(TimeSpan.FromSeconds(0.4), update.EndTime);
            },
            update =>
            {
                Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdated, update.Kind);
                Assert.Null(update.ModelId);
                Assert.Equal("Hello", update.Text);
                Assert.Equal(1, update.AdditionalProperties?["channel_index"]);
            },
            update =>
            {
                Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdated, update.Kind);
                Assert.Null(update.ModelId);
                Assert.Equal("Hello world", update.Text);
            },
            update =>
            {
                Assert.Equal(SpeechToTextResponseUpdateKind.SessionClose, update.Kind);
                Assert.Null(update.ModelId);
            });
    }

    [Fact]
    public async Task GetStreamingTextAsync_WithErrorEvent_YieldsErrorUpdate()
    {
        var webSocket = new FakeWebSocket(
            """{"type":"transcript.created"}""",
            """{"type":"error","message":"bad audio"}""",
            """{"type":"transcript.done","duration":0}""");

        using var stt = new GrokSpeechToTextClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://streaming.test/"),
            "test-api-key",
            (_, _, _) => ValueTask.FromResult<WebSocket>(webSocket));

        var updates = new List<SpeechToTextResponseUpdate>();
        await foreach (var update in stt.GetStreamingTextAsync(new MemoryStream([1])))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, update => update.Kind == SpeechToTextResponseUpdateKind.Error && update.Text == "bad audio");
    }

    [Fact]
    public async Task GetStreamingTextAsync_WithUnsupportedEncoding_ThrowsArgumentException()
    {
        using var stt = new GrokSpeechToTextClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://streaming.test/"),
            "test-api-key",
            (_, _, _) => throw new InvalidOperationException("Should not connect."));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in stt.GetStreamingTextAsync(new MemoryStream([1]),
                new GrokSpeechToTextOptions { AudioFormat = "mp3" }))
            {
            }
        });
    }

    static GrokClientOptions CreateOptions(HttpMessageHandler handler) => new()
    {
        Endpoint = new Uri($"https://unit-{Guid.NewGuid():N}.test/"),
        ChannelOptions = new GrpcChannelOptions
        {
            HttpHandler = handler,
        },
    };

    static void AssertFieldOrder(string body, params string[] fields)
    {
        var previous = -1;
        foreach (var field in fields)
        {
            var current = IndexOfField(body, field);
            Assert.True(current >= 0, $"Field '{field}' was not found in multipart body.");
            Assert.True(current > previous, $"Field '{field}' was not in the expected multipart order.");
            previous = current;
        }
    }

    static int IndexOfField(string body, string field)
    {
        var index = body.IndexOf($"name=\"{field}\"", StringComparison.Ordinal);
        return index >= 0 ? index : body.IndexOf($"name={field}", StringComparison.Ordinal);
    }

    static string GetField(string body, string field)
    {
        var index = IndexOfField(body, field);
        Assert.True(index >= 0, $"Field '{field}' was not found in multipart body.");
        return body[index..Math.Min(body.Length, index + 100)];
    }

    sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null) : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"text":"ok","duration":0}""", Encoding.UTF8, "application/json"),
        });

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    sealed class FakeWebSocket(params string[] messages) : WebSocket
    {
        readonly Queue<byte[]> messages = new(messages.Select(Encoding.UTF8.GetBytes));
        WebSocketState state = WebSocketState.Open;
        WebSocketCloseStatus? closeStatus;
        string? closeStatusDescription;

        public List<string> SentTextMessages { get; } = [];
        public List<byte[]> SentBinaryMessages { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => closeStatus;

        public override string? CloseStatusDescription => closeStatusDescription;

        public override WebSocketState State => state;

        public override string? SubProtocol => null;

        public override void Abort() => state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            this.closeStatus = closeStatus;
            closeStatusDescription = statusDescription;
            state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (messages.Count == 0)
            {
                state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "closed"));
            }

            var message = messages.Dequeue();
            message.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(message.Length, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (messageType == WebSocketMessageType.Text)
            {
                SentTextMessages.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }
            else
            {
                var copy = new byte[buffer.Count];
                Array.Copy(buffer.Array!, buffer.Offset, copy, 0, buffer.Count);
                SentBinaryMessages.Add(copy);
            }

            return Task.CompletedTask;
        }
    }
}
