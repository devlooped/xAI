using System.Buffers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>An xAI real-time speech-to-speech client backed by the documented WebSocket API.</summary>
public sealed class GrokRealtimeClient : IRealtimeClient
{
    const string DefaultModel = "grok-voice-latest";

    readonly HttpClient httpClient;
    readonly Uri endpoint;
    readonly string? apiKey;
    readonly Func<Uri, string?, CancellationToken, ValueTask<WebSocket>> webSocketFactory;

    internal GrokRealtimeClient(HttpMessageHandler handler, Uri endpoint, string? apiKey)
        : this(new HttpClient(handler, disposeHandler: false), endpoint, apiKey, CreateWebSocketAsync)
    {
    }

    internal GrokRealtimeClient(
        HttpClient httpClient,
        Uri endpoint,
        string? apiKey,
        Func<Uri, string?, CancellationToken, ValueTask<WebSocket>> webSocketFactory)
    {
        this.httpClient = Throw.IfNull(httpClient);
        this.endpoint = Throw.IfNull(endpoint);
        this.apiKey = apiKey;
        this.webSocketFactory = Throw.IfNull(webSocketFactory);
    }

    /// <summary>Creates an ephemeral client secret using the configured API key.</summary>
    /// <param name="expiresAfter">The secret lifetime. xAI accepts whole seconds up to one hour; the default is ten minutes.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The ephemeral token value.</returns>
    public async Task<string> CreateEphemeralTokenAsync(
        TimeSpan? expiresAfter = null,
        CancellationToken cancellationToken = default)
    {
        var lifetime = expiresAfter ?? TimeSpan.FromMinutes(10);
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromHours(1) ||
            lifetime.TotalSeconds != Math.Truncate(lifetime.TotalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAfter), "The xAI ephemeral token lifetime must be a whole number of seconds from 1 through 3600.");
        }

        var requestUri = GetEndpoint("https", "v1/realtime/client_secrets");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new { expires_after = new { seconds = (int)lifetime.TotalSeconds } }),
        };

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            await ThrowHttpExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var token = await response.Content.ReadFromJsonAsync<EphemeralTokenResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("xAI ephemeral token response body was empty.");

        if (string.IsNullOrWhiteSpace(token.Value))
            throw new InvalidOperationException("xAI ephemeral token response did not contain a token value.");

        return token.Value;
    }

    /// <inheritdoc />
    public async Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RealtimeSessionOptions();
        ValidateOptions(options);

        var grokOptions = options as GrokRealtimeOptions;
        var token = grokOptions?.EphemeralToken ?? apiKey;
        var model = options.Model ?? DefaultModel;
        var uri = GetRealtimeEndpoint(model);
        var webSocket = await webSocketFactory(uri, token, cancellationToken).ConfigureAwait(false);
        var session = new GrokRealtimeClientSession(webSocket, options);

        try
        {
            await session.SendSessionUpdateAsync(options, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose() => httpClient.Dispose();

    Uri GetRealtimeEndpoint(string model)
    {
        var query = $"model={Uri.EscapeDataString(model)}";
        return GetEndpoint(endpoint.Scheme == Uri.UriSchemeHttp ? "ws" : "wss", "v1/realtime", query);
    }

    Uri GetEndpoint(string scheme, string path, string? query = null)
    {
        var basePath = endpoint.AbsolutePath == "/" ? "" : endpoint.AbsolutePath.TrimEnd('/');
        return new UriBuilder(endpoint)
        {
            Scheme = scheme,
            Path = $"{basePath}/{path.TrimStart('/')}",
            Query = query ?? "",
        }.Uri;
    }

    internal static void ValidateOptions(RealtimeSessionOptions options)
    {
        if (options.SessionKind != RealtimeSessionKind.Conversation)
            throw new NotSupportedException("xAI's realtime speech-to-speech endpoint does not expose MEAI transcription-only sessions.");

        if (options.OutputModalities is not null)
            throw new NotSupportedException("xAI's realtime API does not document session-level output_modalities.");

        if (options.ToolMode is not null)
            throw new NotSupportedException("xAI's realtime API does not document MEAI tool-choice modes.");

        if (options.TranscriptionOptions is not null)
            throw new NotSupportedException("xAI's realtime speech-to-speech API does not document MEAI transcription-only settings.");

        if (options is GrokRealtimeOptions { EphemeralToken: not null and var ephemeralToken } &&
            string.IsNullOrWhiteSpace(ephemeralToken))
        {
            throw new ArgumentException("An ephemeral token cannot be empty or whitespace.", nameof(options));
        }

        if (options is GrokRealtimeOptions { CustomVoiceId: { Length: > 0 } customVoice } &&
            !string.IsNullOrEmpty(options.Voice) &&
            !string.Equals(customVoice, options.Voice, StringComparison.Ordinal))
        {
            throw new ArgumentException("Set either Voice or CustomVoiceId, not both.", nameof(options));
        }
    }

    static async ValueTask<WebSocket> CreateWebSocketAsync(Uri uri, string? token, CancellationToken cancellationToken)
    {
        var webSocket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(token))
            webSocket.Options.SetRequestHeader("Authorization", string.Concat("Bearer ", token));

        await webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        return webSocket;
    }

    static async Task ThrowHttpExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body) ?
            $"xAI realtime token request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase})." :
            $"xAI realtime token request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}): {body}";
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    sealed record EphemeralTokenResponse(string? Value, long ExpiresAt);
}

sealed class GrokRealtimeClientSession(WebSocket webSocket, RealtimeSessionOptions options) : IRealtimeClientSession
{
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly HashSet<string> emittedFunctionCalls = new(StringComparer.Ordinal);
    int disposed;

    /// <inheritdoc />
    public RealtimeSessionOptions? Options { get; private set; } = options;

    internal async Task SendSessionUpdateAsync(RealtimeSessionOptions updatedOptions, CancellationToken cancellationToken)
    {
        await SendJsonAsync(updatedOptions.ToRealtimeSessionUpdate(), cancellationToken).ConfigureAwait(false);
        Options = updatedOptions;
    }

    /// <inheritdoc />
    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(message);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var json = message switch
        {
            SessionUpdateRealtimeClientMessage update => UpdateSession(update.Options),
            InputAudioBufferAppendRealtimeClientMessage audio => audio.ToRealtimeAudioAppend(),
            InputAudioBufferCommitRealtimeClientMessage => new JsonObject { ["type"] = "input_audio_buffer.commit" },
            CreateConversationItemRealtimeClientMessage item => item.Item.ToRealtimeConversationItem(),
            CreateResponseRealtimeClientMessage response => response.ToRealtimeResponse(),
            _ => message.ToRealtimeRawMessage(),
        };

        if (message.MessageId is { Length: > 0 } messageId)
            json["event_id"] = messageId;

        await SendJsonAsync(json, cancellationToken).ConfigureAwait(false);

        if (message is SessionUpdateRealtimeClientMessage sessionUpdate)
            Options = sessionUpdate.Options;
    }

    static JsonObject UpdateSession(RealtimeSessionOptions options)
    {
        GrokRealtimeClient.ValidateOptions(options);
        return options.ToRealtimeSessionUpdate();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        while (true)
        {
            using var json = await ReceiveJsonAsync(webSocket, cancellationToken).ConfigureAwait(false);
            if (json is null)
                yield break;

            var message = json.RootElement.ToRealtimeServerMessage();
            if (message is ResponseOutputItemRealtimeServerMessage { Item.Contents: var contents } &&
                contents.OfType<FunctionCallContent>().FirstOrDefault() is { CallId.Length: > 0 } functionCall &&
                !emittedFunctionCalls.Add(functionCall.CallId))
            {
                continue;
            }

            yield return message;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session closed", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                webSocket.Abort();
            }
        }
        webSocket.Dispose();
    }

    async Task SendJsonAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendLock.Release();
        }
    }

    static async Task<JsonDocument?> ReceiveJsonAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidOperationException($"xAI realtime returned unsupported WebSocket message type: {result.MessageType}.");

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    break;
            }

            stream.Position = 0;
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

}
