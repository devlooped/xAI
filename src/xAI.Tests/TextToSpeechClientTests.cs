using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Grpc.Net.Client;
using Microsoft.Extensions.AI;

namespace xAI.Tests;

public class TextToSpeechClientTests
{
    [Fact]
    public void AsITextToSpeechClient_ReturnsMetadata()
    {
        using var client = new GrokClient("test-api-key", CreateOptions(new CaptureHandler()));
        using var tts = client.AsITextToSpeechClient();

        var metadata = tts.GetService<TextToSpeechClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("xai", metadata.ProviderName);
        Assert.Equal(client.Options.Endpoint, metadata.ProviderUri);
        Assert.Null(metadata.DefaultModelId);
    }

    [Fact]
    public async Task GetAudioAsync_MapsRequestAndResponse()
    {
        var audio = new byte[] { 1, 2, 3 };
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(audio)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue("audio/wav"),
                }
            }
        });

        using var client = new GrokClient("test-api-key", CreateOptions(handler));
        using var tts = client.AsITextToSpeechClient();

        var response = await tts.GetAudioAsync("Hello from Grok.",
            new GrokTextToSpeechOptions
            {
                VoiceId = "rex",
                Language = "pt-BR",
                AudioFormat = "audio/wav",
                SampleRate = 44100,
                BitRate = 192000,
                OptimizeStreamingLatency = 1,
                TextNormalization = true,
                ModelId = "test-model",
            });

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(new Uri($"{client.Options.Endpoint}v1/tts"), handler.Request.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("test-api-key", handler.Request.Headers.Authorization?.Parameter);

        using var json = JsonDocument.Parse(handler.RequestBody!);
        var root = json.RootElement;
        Assert.Equal("Hello from Grok.", root.GetProperty("text").GetString());
        Assert.Equal("rex", root.GetProperty("voice_id").GetString());
        Assert.Equal("pt-BR", root.GetProperty("language").GetString());
        Assert.Equal(1, root.GetProperty("optimize_streaming_latency").GetInt32());
        Assert.True(root.GetProperty("text_normalization").GetBoolean());

        var outputFormat = root.GetProperty("output_format");
        Assert.Equal("wav", outputFormat.GetProperty("codec").GetString());
        Assert.Equal(44100, outputFormat.GetProperty("sample_rate").GetInt32());
        Assert.Equal(192000, outputFormat.GetProperty("bit_rate").GetInt32());

        var content = Assert.Single(response.Contents);
        var data = Assert.IsType<DataContent>(content);
        Assert.Equal("audio/wav", data.MediaType);
        Assert.Equal(audio, data.Data.ToArray());
        Assert.Equal("test-model", response.ModelId);
    }

    [Theory]
    [InlineData(null, "audio/mpeg")]
    [InlineData("mp3", "audio/mpeg")]
    [InlineData("wav", "audio/wav")]
    [InlineData("pcm", "audio/pcm")]
    [InlineData("mulaw", "audio/basic")]
    [InlineData("alaw", "audio/alaw")]
    public async Task GetAudioAsync_MapsCodecToMediaType(string? audioFormat, string expectedMediaType)
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
        });

        using var client = new GrokClient("test-api-key", CreateOptions(handler));
        using var tts = client.AsITextToSpeechClient();

        var response = await tts.GetAudioAsync("Hello.", new TextToSpeechOptions { AudioFormat = audioFormat });

        var data = Assert.IsType<DataContent>(Assert.Single(response.Contents));
        Assert.Equal(expectedMediaType, data.MediaType);
    }

    [Fact]
    public async Task GetAudioAsync_WithDefaults_SendsRequiredFieldsOnly()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
        });

        using var client = new GrokClient("test-api-key", CreateOptions(handler));
        using var tts = client.AsITextToSpeechClient();

        await tts.GetAudioAsync("Hello.");

        using var json = JsonDocument.Parse(handler.RequestBody!);
        var root = json.RootElement;
        Assert.Equal("Hello.", root.GetProperty("text").GetString());
        Assert.Equal("eve", root.GetProperty("voice_id").GetString());
        Assert.Equal("en", root.GetProperty("language").GetString());
        Assert.False(root.TryGetProperty("output_format", out _));
    }

    [Fact]
    public async Task GetAudioAsync_WithError_ThrowsHttpRequestException()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent("""{"error":"invalid language"}"""),
        });

        using var client = new GrokClient("test-api-key", CreateOptions(handler));
        using var tts = client.AsITextToSpeechClient();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => tts.GetAudioAsync("Hello."));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("invalid language", exception.Message);
    }

    [Fact]
    public async Task GetAudioAsync_WithNullText_ThrowsArgumentNullException()
    {
        using var client = new GrokClient("test-api-key", CreateOptions(new CaptureHandler()));
        using var tts = client.AsITextToSpeechClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => tts.GetAudioAsync(null!));
    }

    [Fact]
    public async Task GetStreamingAudioAsync_MapsWebSocketEvents()
    {
        var webSocket = new FakeWebSocket(
            """{"type":"audio.delta","delta":"AQID"}""",
            """{"type":"audio.done","trace_id":"trace-123"}""");

        Uri? capturedUri = null;
        string? capturedApiKey = null;
        using var tts = new GrokTextToSpeechClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://streaming.test/base/"),
            "test-api-key",
            (uri, apiKey, _) =>
            {
                capturedUri = uri;
                capturedApiKey = apiKey;
                return ValueTask.FromResult<WebSocket>(webSocket);
            });

        var updates = new List<TextToSpeechResponseUpdate>();
        await foreach (var update in tts.GetStreamingAudioAsync("Hello.",
            new GrokTextToSpeechOptions
            {
                VoiceId = "ara",
                Language = "auto",
                AudioFormat = "mulaw",
                SampleRate = 8000,
                OptimizeStreamingLatency = 1,
                TextNormalization = true,
            }))
        {
            updates.Add(update);
        }

        Assert.Equal("test-api-key", capturedApiKey);
        Assert.Equal("wss://streaming.test/base/v1/tts?voice=ara&language=auto&codec=mulaw&sample_rate=8000&optimize_streaming_latency=1&text_normalization=true", capturedUri!.ToString());

        Assert.Collection(webSocket.SentMessages,
            message =>
            {
                using var json = JsonDocument.Parse(message);
                Assert.Equal("text.delta", json.RootElement.GetProperty("type").GetString());
                Assert.Equal("Hello.", json.RootElement.GetProperty("delta").GetString());
            },
            message =>
            {
                using var json = JsonDocument.Parse(message);
                Assert.Equal("text.done", json.RootElement.GetProperty("type").GetString());
            });

        Assert.Collection(updates,
            update =>
            {
                Assert.Equal(TextToSpeechResponseUpdateKind.AudioUpdating, update.Kind);
                var data = Assert.IsType<DataContent>(Assert.Single(update.Contents));
                Assert.Equal(new byte[] { 1, 2, 3 }, data.Data.ToArray());
                Assert.Equal("audio/basic", data.MediaType);
            },
            update =>
            {
                Assert.Equal(TextToSpeechResponseUpdateKind.SessionClose, update.Kind);
                Assert.Equal("trace-123", update.AdditionalProperties?["trace_id"]);
            });
    }

    [Fact]
    public async Task GetStreamingAudioAsync_WithErrorEvent_ThrowsInvalidOperationException()
    {
        var webSocket = new FakeWebSocket("""{"type":"error","message":"voice rejected"}""");
        using var tts = new GrokTextToSpeechClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://streaming.test/"),
            "test-api-key",
            (_, _, _) => ValueTask.FromResult<WebSocket>(webSocket));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in tts.GetStreamingAudioAsync("Hello."))
            {
            }
        });

        Assert.Contains("voice rejected", exception.Message);
    }

    [SecretsTheory("XAI_API_KEY")]
    //[InlineData("ara")]
    //[InlineData("eve")]
    [InlineData("rex")] // 👈 el mejor para Jesus
    //[InlineData("sal")]
    //[InlineData("leo")]
    public async Task GetStreamingAudioAsync_IntegrationTest_SavesAndPlaysAudio(string voiceId)
    {
        var apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY");
        using var client = new GrokClient(apiKey!);
        using var tts = client.AsITextToSpeechClient();

        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xai-tts-{Guid.NewGuid():N}.mp3");

        await using (var fileStream = System.IO.File.Create(tempFile))
        {
            await foreach (var update in tts.GetStreamingAudioAsync(
                """
                El que cree en mí, en realidad no cree en mí, sino en aquel que me envió. 
                Y el que me ve, ve al que me envió. 
                Yo soy la luz, y he venido al mundo para que todo el que crea en mí no permanezca en las tinieblas.
                """,
                new GrokTextToSpeechOptions
                {
                    VoiceId = voiceId,
                    AudioFormat = "mp3",

                }))
            {
                if (update.Kind == TextToSpeechResponseUpdateKind.AudioUpdating)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is DataContent data)
                        {
                            await fileStream.WriteAsync(data.Data);
                        }
                    }
                }
            }
        }

        Assert.True(System.IO.File.Exists(tempFile));
        Assert.True(new System.IO.FileInfo(tempFile).Length > 0);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = tempFile,
            UseShellExecute = true
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

    sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null) : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> responder = responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
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

        public List<string> SentMessages { get; } = [];

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
            SentMessages.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }
}
