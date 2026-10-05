using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Grpc.Net.Client;
using Microsoft.Extensions.AI;
using static ConfigurationExtensions;

namespace xAI.Tests;

public class RealtimeClientTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CreateSessionAsync_UsesRealtimeEndpointAndMapsOptionsAndMessages()
    {
        var socket = new FakeWebSocket(
            """{"type":"response.created","event_id":"evt-1","response":{"id":"resp-1"}}""",
            """{"type":"response.output_text.delta","event_id":"evt-2","response_id":"resp-1","item_id":"item-1","output_index":0,"content_index":0,"delta":"Hello"}""",
            """{"type":"response.output_audio.delta","response_id":"resp-1","delta":"AQID"}""",
            """{"type":"response.output_item.done","response_id":"resp-1","output_index":1,"item":{"id":"item-function","type":"function_call","call_id":"call-1","name":"weather","arguments":"{\"city\":\"Paris\"}"}}""",
            """{"type":"response.function_call_arguments.done","response_id":"resp-1","output_index":1,"call_id":"call-1","name":"weather","arguments":"{\"city\":\"Paris\"}"}""",
            """{"type":"error","error":{"message":"bad request","event_id":"evt-3"}}""");
        Uri? capturedUri = null;
        string? capturedToken = null;

        using var client = new GrokRealtimeClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://realtime.test/base/"),
            "api-key",
            (uri, token, _) =>
            {
                capturedUri = uri;
                capturedToken = token;
                return ValueTask.FromResult<WebSocket>(socket);
            });

        await using var session = await client.CreateSessionAsync(new GrokRealtimeOptions
        {
            Model = "grok-voice-think-fast-2.0",
            Instructions = "Be helpful.",
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = true },
            Tools = [AIFunctionFactory.Create((string city) => city, "weather", "Look up weather.")],
            EphemeralToken = "ephemeral-secret",
            CustomVoiceId = "custom-voice-id",
            EnableResumption = true,
        });

        Assert.Equal("wss://realtime.test/base/v1/realtime?model=grok-voice-think-fast-2.0", capturedUri!.AbsoluteUri);
        Assert.Equal("ephemeral-secret", capturedToken);
        Assert.Collection(socket.SentTextMessages,
            message =>
            {
                using var json = JsonDocument.Parse(message);
                var sessionOptions = json.RootElement.GetProperty("session");
                Assert.Equal("custom-voice-id", sessionOptions.GetProperty("voice").GetString());
                Assert.Equal("Be helpful.", sessionOptions.GetProperty("instructions").GetString());
                Assert.Equal("audio/pcm", sessionOptions.GetProperty("audio").GetProperty("input").GetProperty("format").GetProperty("type").GetString());
                Assert.Equal(24000, sessionOptions.GetProperty("audio").GetProperty("output").GetProperty("format").GetProperty("rate").GetInt32());
                Assert.Equal("server_vad", sessionOptions.GetProperty("turn_detection").GetProperty("type").GetString());
                Assert.True(sessionOptions.GetProperty("resumption").GetProperty("enabled").GetBoolean());
                Assert.Equal("function", sessionOptions.GetProperty("tools")[0].GetProperty("type").GetString());
                Assert.Equal("weather", sessionOptions.GetProperty("tools")[0].GetProperty("name").GetString());
            });

        await session.SendAsync(new InputAudioBufferAppendRealtimeClientMessage(new DataContent(new byte[] { 1, 2, 3 }, "audio/pcm")));
        await session.SendAsync(new InputAudioBufferCommitRealtimeClientMessage());
        await session.SendAsync(new CreateConversationItemRealtimeClientMessage(
            new RealtimeConversationItem([new TextContent("Hi!")], role: ChatRole.User)));
        await session.SendAsync(new CreateResponseRealtimeClientMessage { Instructions = "Answer briefly." });

        using (var audioAppend = JsonDocument.Parse(socket.SentTextMessages[1]))
        {
            Assert.Equal("input_audio_buffer.append", audioAppend.RootElement.GetProperty("type").GetString());
            Assert.Equal("AQID", audioAppend.RootElement.GetProperty("audio").GetString());
        }
        using (var item = JsonDocument.Parse(socket.SentTextMessages[3]))
        {
            Assert.Equal("input_text", item.RootElement.GetProperty("item").GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("Hi!", item.RootElement.GetProperty("item").GetProperty("content")[0].GetProperty("text").GetString());
        }
        using (var response = JsonDocument.Parse(socket.SentTextMessages[4]))
        {
            Assert.Equal("Answer briefly.", response.RootElement.GetProperty("response").GetProperty("instructions").GetString());
        }

        var serverMessages = new List<RealtimeServerMessage>();
        await foreach (var message in session.GetStreamingResponseAsync())
            serverMessages.Add(message);

        Assert.Collection(serverMessages,
            message =>
            {
                Assert.Equal(RealtimeServerMessageType.ResponseCreated, message.Type);
                Assert.IsType<ResponseCreatedRealtimeServerMessage>(message);
                Assert.Equal("resp-1", ((ResponseCreatedRealtimeServerMessage)message).ResponseId);
            },
            message =>
            {
                var text = Assert.IsType<OutputTextAudioRealtimeServerMessage>(message);
                Assert.Equal(RealtimeServerMessageType.OutputTextDelta, text.Type);
                Assert.Equal("Hello", text.Text);
                Assert.Equal("evt-2", text.MessageId);
            },
            message =>
            {
                var audio = Assert.IsType<OutputTextAudioRealtimeServerMessage>(message);
                Assert.Equal(RealtimeServerMessageType.OutputAudioDelta, audio.Type);
                Assert.Equal("AQID", audio.Audio);
            },
            message =>
            {
                var outputItem = Assert.IsType<ResponseOutputItemRealtimeServerMessage>(message);
                Assert.Equal(RealtimeServerMessageType.ResponseOutputItemDone, outputItem.Type);
                var functionCall = Assert.IsType<FunctionCallContent>(Assert.Single(outputItem.Item!.Contents));
                Assert.Equal("call-1", functionCall.CallId);
                Assert.Equal("weather", functionCall.Name);
            },
            message =>
            {
                var error = Assert.IsType<ErrorRealtimeServerMessage>(message);
                Assert.Equal("bad request", error.Error?.Message);
                Assert.Equal("evt-3", error.OriginatingMessageId);
            });
    }

    [Fact]
    public async Task CreateEphemeralTokenAsync_UsesAuthenticatedEndpointAndExpiration()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":"short-lived-token","expires_at":1750000000}""", Encoding.UTF8, "application/json"),
        });

        using var client = new GrokRealtimeClient(
            new HttpClient(handler),
            new Uri("https://realtime.test/root/"),
            "api-key",
            (_, _, _) => throw new InvalidOperationException("Token creation does not open a WebSocket."));

        var token = await client.CreateEphemeralTokenAsync(TimeSpan.FromMinutes(5));

        Assert.Equal("short-lived-token", token);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(new Uri("https://realtime.test/root/v1/realtime/client_secrets"), handler.Request.RequestUri);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(300, body.RootElement.GetProperty("expires_after").GetProperty("seconds").GetInt32());
    }

    [SecretsFact("CI_XAI_API_KEY")]
    public async Task CreateSessionAsync_WithTextTurn_ReturnsAssistantOutput()
    {
        using var client = new GrokClient(Configuration["CI_XAI_API_KEY"]!);
        using var realtime = client.AsIRealtimeClient();

        await using var session = await realtime.CreateSessionAsync(new GrokRealtimeOptions
        {
            Voice = "eve",
            Instructions = "Reply with a short spoken greeting.",
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = false },
        });

        await session.SendAsync(new CreateConversationItemRealtimeClientMessage(
            new RealtimeConversationItem([new TextContent("Say hello.")], role: ChatRole.User)));
        await session.SendAsync(new CreateResponseRealtimeClientMessage());

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var sawAssistantOutput = false;
        await foreach (var message in session.GetStreamingResponseAsync(timeout.Token))
        {
            output.WriteLine(message.Type.ToString());
            if (message is ErrorRealtimeServerMessage error)
                throw new InvalidOperationException(error.Error?.Message ?? "xAI realtime server error.");

            if (message is OutputTextAudioRealtimeServerMessage outputMessage &&
                (outputMessage.Text is { Length: > 0 } || outputMessage.Audio is { Length: > 0 }))
            {
                sawAssistantOutput = true;
            }

            if (message.Type == RealtimeServerMessageType.ResponseDone)
            {
                Assert.True(sawAssistantOutput, "Expected assistant text or audio before the realtime response completed.");
                return;
            }
        }

        Assert.Fail("Realtime session closed before response.done.");
    }

    [Fact]
    public async Task CreateSessionAsync_WithConflictingVoicesThrowsBeforeConnecting()
    {
        using var client = new GrokRealtimeClient(
            new HttpClient(new CaptureHandler()),
            new Uri("https://realtime.test/"),
            "api-key",
            (_, _, _) => throw new InvalidOperationException("Invalid voice options should not connect."));

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateSessionAsync(new GrokRealtimeOptions
        {
            Voice = "eve",
            CustomVoiceId = "custom",
        }));
    }

    sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null) : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> respond = responder ?? (_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":"token","expires_at":1750000000}"""),
            });

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    sealed class FakeWebSocket(params string[] messages) : WebSocket
    {
        readonly Queue<byte[]> incoming = new(messages.Select(Encoding.UTF8.GetBytes));
        WebSocketState state = WebSocketState.Open;
        WebSocketCloseStatus? closeStatus;

        public List<string> SentTextMessages { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => closeStatus;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => state;
        public override string? SubProtocol => null;

        public override void Abort() => state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellationToken)
        {
            closeStatus = status;
            state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellationToken)
            => CloseAsync(status, description, cancellationToken);

        public override void Dispose() => state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (incoming.Count == 0)
            {
                state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "done"));
            }

            var message = incoming.Dequeue();
            message.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(message.Length, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (messageType != WebSocketMessageType.Text)
                throw new InvalidOperationException($"Unexpected message type: {messageType}.");

            SentTextMessages.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }
}
