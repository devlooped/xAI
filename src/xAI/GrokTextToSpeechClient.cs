using System.Buffers;
using System.Collections.Specialized;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Represents an <see cref="ITextToSpeechClient"/> for xAI's Grok text to speech service.</summary>
partial class GrokTextToSpeechClient : ITextToSpeechClient
{
    const string DefaultVoice = "eve";
    const string DefaultLanguage = "en";
    const string DefaultCodec = "mp3";


    readonly TextToSpeechClientMetadata metadata;
    readonly HttpClient httpClient;
    readonly Uri endpoint;
    readonly string? apiKey;
    readonly Func<Uri, string?, CancellationToken, ValueTask<WebSocket>> webSocketFactory;

    internal GrokTextToSpeechClient(HttpMessageHandler handler, GrokClientOptions options, string? apiKey)
        : this(new HttpClient(handler, disposeHandler: false), options.Endpoint, apiKey, CreateWebSocketAsync)
    {
    }

    internal GrokTextToSpeechClient(
        HttpClient httpClient,
        Uri endpoint,
        string? apiKey,
        Func<Uri, string?, CancellationToken, ValueTask<WebSocket>> webSocketFactory)
    {
        this.httpClient = Throw.IfNull(httpClient);
        this.endpoint = Throw.IfNull(endpoint);
        this.apiKey = apiKey;
        this.webSocketFactory = Throw.IfNull(webSocketFactory);

        metadata = new("xai", endpoint);
    }

    /// <inheritdoc />
    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(Throw.IfNull(text), options);
        using var message = new HttpRequestMessage(HttpMethod.Post, GetHttpEndpoint())
        {
            Content = JsonContent.Create(request, JsonContext.Default.GrokTextToSpeechRequest),
        };

        using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowHttpExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? GetMediaType(request.OutputFormat?.Codec);

        var raw = new HttpResponseMessage(response.StatusCode);
        foreach (var header in response.Headers)
            raw.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var header in response.Content.Headers)
            raw.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return new TextToSpeechResponse([new DataContent(audio, mediaType)])
        {
            ModelId = options?.ModelId,
            RawRepresentation = raw,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(Throw.IfNull(text), options);
        using var webSocket = await webSocketFactory(GetStreamingEndpoint(request), apiKey, cancellationToken).ConfigureAwait(false);

        await SendJsonAsync(webSocket, new TextDeltaMessage(text), JsonContext.Default.TextDeltaMessage, cancellationToken).ConfigureAwait(false);
        await SendJsonAsync(webSocket, TextDoneMessage.Instance, JsonContext.Default.TextDoneMessage, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            using var json = await ReceiveJsonAsync(webSocket, cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            var rawRepresentation = root.Clone();
            var type = GetRequiredString(root, "type");

            switch (type)
            {
                case "audio.delta":
                    var audio = Convert.FromBase64String(GetRequiredString(root, "delta"));
                    yield return new TextToSpeechResponseUpdate
                    {
                        Kind = TextToSpeechResponseUpdateKind.AudioUpdating,
                        Contents = [new DataContent(audio, GetMediaType(request.OutputFormat?.Codec))],
                        ModelId = options?.ModelId,
                        RawRepresentation = rawRepresentation,
                    };
                    break;

                case "audio.done":
                    var update = new TextToSpeechResponseUpdate
                    {
                        Kind = TextToSpeechResponseUpdateKind.SessionClose,
                        ModelId = options?.ModelId,
                        RawRepresentation = rawRepresentation,
                    };

                    if (root.TryGetProperty("trace_id", out var traceId) && traceId.ValueKind == JsonValueKind.String)
                    {
                        update.AdditionalProperties = new()
                        {
                            ["trace_id"] = traceId.GetString(),
                        };
                    }

                    yield return update;
                    yield break;

                case "error":
                    throw new InvalidOperationException($"xAI TTS streaming error: {GetRequiredString(root, "message")}");

                default:
                    throw new InvalidOperationException($"Unsupported xAI TTS streaming event type: {type}");
            }
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is not null ? null : serviceType switch
    {
        Type t when t == typeof(TextToSpeechClientMetadata) => metadata,
        Type t when t == typeof(GrokTextToSpeechClient) => this,
        Type t when t == typeof(HttpClient) => httpClient,
        Type t when t.IsInstanceOfType(this) => this,
        _ => null
    };

    /// <inheritdoc />
    public void Dispose() => httpClient.Dispose();

    static GrokTextToSpeechRequest CreateRequest(string text, TextToSpeechOptions? options)
    {
        var codec = GetCodec(options?.AudioFormat);
        var grokOptions = options as GrokTextToSpeechOptions;
        var outputFormat =
            codec != DefaultCodec ||
            grokOptions?.SampleRate is not null ||
            grokOptions?.BitRate is not null
                ? new GrokTextToSpeechOutputFormat(codec, grokOptions?.SampleRate, grokOptions?.BitRate)
                : null;

        return new(
            text,
            options?.VoiceId ?? DefaultVoice,
            options?.Language ?? DefaultLanguage,
            outputFormat,
            grokOptions?.OptimizeStreamingLatency,
            grokOptions?.TextNormalization);
    }

    Uri GetHttpEndpoint() => GetEndpoint(endpoint, "https", "v1/tts", null);

    Uri GetStreamingEndpoint(GrokTextToSpeechRequest request)
    {
        var query = new NameValueCollection
        {
            ["voice"] = request.VoiceId,
            ["language"] = request.Language,
            ["codec"] = request.OutputFormat?.Codec ?? DefaultCodec,
        };

        if (request.OutputFormat?.SampleRate is int sampleRate)
            query["sample_rate"] = sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (request.OutputFormat?.BitRate is int bitRate)
            query["bit_rate"] = bitRate.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (request.OptimizeStreamingLatency is int optimizeStreamingLatency)
            query["optimize_streaming_latency"] = optimizeStreamingLatency.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (request.TextNormalization is bool textNormalization)
            query["text_normalization"] = textNormalization ? "true" : "false";

        return GetEndpoint(endpoint, endpoint.Scheme == Uri.UriSchemeHttp ? "ws" : "wss", "v1/tts", query);
    }

    static Uri GetEndpoint(Uri endpoint, string scheme, string relativePath, NameValueCollection? query) => new UriBuilder(endpoint)
    {
        Scheme = scheme,
        Path = CombinePath(endpoint.AbsolutePath, relativePath),
        Query = query is null ? "" : ToQueryString(query),
    }.Uri;

    static string CombinePath(string basePath, string relativePath)
    {
        var path = basePath == "/" ? "" : basePath.TrimEnd('/');
        return $"{path}/{relativePath.TrimStart('/')}";
    }

    static string ToQueryString(NameValueCollection query)
    {
        var builder = new StringBuilder();

        foreach (string key in query)
        {
            if (query[key] is not { } value)
                continue;

            if (builder.Length > 0)
                builder.Append('&');

            builder
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    static string GetCodec(string? format) => format?.ToUpperInvariant() switch
    {
        null or "" => DefaultCodec,
        "MP3" or "AUDIO/MPEG" => "mp3",
        "WAV" or "AUDIO/WAV" => "wav",
        "PCM" or "AUDIO/PCM" or "AUDIO/L16" => "pcm",
        "MULAW" or "ULAW" or "AUDIO/BASIC" => "mulaw",
        "ALAW" or "AUDIO/ALAW" => "alaw",
        _ => format.ToLowerInvariant(),
    };

    static string GetMediaType(string? codec) => codec switch
    {
        null or "" or "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "pcm" => "audio/pcm",
        "mulaw" or "ulaw" => "audio/basic",
        "alaw" => "audio/alaw",
        _ => "application/octet-stream",
    };

    static async Task ThrowHttpExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body) ?
            $"xAI TTS request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase})." :
            $"xAI TTS request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}): {body}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    static async ValueTask<WebSocket> CreateWebSocketAsync(Uri uri, string? apiKey, CancellationToken cancellationToken)
    {
        var webSocket = new ClientWebSocket();

        if (!string.IsNullOrEmpty(apiKey))
            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        await webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        return webSocket;
    }

    static Task SendJsonAsync<T>(WebSocket webSocket, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        => webSocket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), WebSocketMessageType.Text, true, cancellationToken);

    static async Task<JsonDocument> ReceiveJsonAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var stream = new MemoryStream();

            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException($"xAI TTS streaming connection closed before audio.done: {result.CloseStatusDescription ?? result.CloseStatus?.ToString()}");

                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidOperationException($"xAI TTS streaming returned unsupported message type: {result.MessageType}");

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

    static string GetRequiredString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"xAI TTS streaming event is missing required string property '{propertyName}'.");

        return property.GetString()!;
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(GrokTextToSpeechRequest))]
    [JsonSerializable(typeof(TextDeltaMessage))]
    [JsonSerializable(typeof(TextDoneMessage))]
    partial class JsonContext : JsonSerializerContext { }

    sealed record GrokTextToSpeechRequest(string Text, string VoiceId, string Language,
        GrokTextToSpeechOutputFormat? OutputFormat, int? OptimizeStreamingLatency, bool? TextNormalization);

    sealed record GrokTextToSpeechOutputFormat(string Codec, int? SampleRate, int? BitRate);

    sealed record TextDeltaMessage(string Delta)
    {
        public string Type => "text.delta";
    }

    sealed record TextDoneMessage
    {
        public static readonly TextDoneMessage Instance = new();

        public string Type => "text.done";
    }
}
