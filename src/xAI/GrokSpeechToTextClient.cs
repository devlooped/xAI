using System.Buffers;
using System.Collections.Specialized;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Represents an <see cref="ISpeechToTextClient"/> for xAI's Grok speech to text service.</summary>
partial class GrokSpeechToTextClient : ISpeechToTextClient
{
    const string DefaultFilename = "audio.mp3";
    const string DefaultStreamingEncoding = "pcm";
    const int DefaultStreamingSampleRate = 16000;
    const int DefaultStreamingChunkSize = 8192;

    static readonly Dictionary<string, string> extensionToMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".flac"] = "audio/flac",
        [".aac"] = "audio/aac",
        [".mp4"] = "audio/mp4",
        [".m4a"] = "audio/mp4",
        [".mkv"] = "video/x-matroska",
    };

    readonly SpeechToTextClientMetadata metadata;
    readonly HttpClient httpClient;
    readonly Uri endpoint;
    readonly string? apiKey;
    readonly Func<Uri, string?, CancellationToken, ValueTask<WebSocket>> webSocketFactory;

    internal GrokSpeechToTextClient(HttpMessageHandler handler, GrokClientOptions options, string? apiKey)
        : this(new HttpClient(handler, disposeHandler: false), options.Endpoint, apiKey, CreateWebSocketAsync)
    {
    }

    internal GrokSpeechToTextClient(
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
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(audioSpeechStream);

        using var message = new HttpRequestMessage(HttpMethod.Post, GetHttpEndpoint())
        {
            Content = CreateMultipartContent(audioSpeechStream, options),
        };

        using var response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowHttpExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var transcript = await response.Content.ReadFromJsonAsync(SpeechToTextJsonContext.Default.GrokSpeechToTextResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("xAI STT response body was empty.");

        return ToSpeechToTextResponse(transcript, options);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(audioSpeechStream);

        using var webSocket = await webSocketFactory(GetStreamingEndpoint(options), apiKey, cancellationToken).ConfigureAwait(false);

        using (var ready = await ReceiveJsonAsync(webSocket, cancellationToken).ConfigureAwait(false))
        {
            var root = ready.RootElement;
            var rawRepresentation = root.Clone();
            var type = GetRequiredString(root, "type");

            if (type != "transcript.created")
                throw new InvalidOperationException($"Expected xAI STT streaming event type 'transcript.created' but received '{type}'.");

            yield return new SpeechToTextResponseUpdate
            {
                Kind = SpeechToTextResponseUpdateKind.SessionOpen,
                RawRepresentation = rawRepresentation,
            };
        }

        await SendAudioAsync(webSocket, audioSpeechStream, cancellationToken).ConfigureAwait(false);
        await SendJsonAsync(webSocket, AudioDoneMessage.Instance, SpeechToTextJsonContext.Default.AudioDoneMessage, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            using var json = await ReceiveJsonAsync(webSocket, cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            var rawRepresentation = root.Clone();
            var type = GetRequiredString(root, "type");

            switch (type)
            {
                case "transcript.partial":
                    yield return CreateTextUpdate(root, rawRepresentation, options);
                    break;

                case "transcript.done":
                    if (TryGetString(root, "text") is { Length: > 0 })
                        yield return CreateTextUpdate(root, rawRepresentation, options, SpeechToTextResponseUpdateKind.TextUpdated);

                    yield return new SpeechToTextResponseUpdate
                    {
                        Kind = SpeechToTextResponseUpdateKind.SessionClose,
                        RawRepresentation = rawRepresentation,
                        AdditionalProperties = CreateStreamingAdditionalProperties(root),
                    };
                    yield break;

                case "error":
                    yield return new SpeechToTextResponseUpdate
                    {
                        Kind = SpeechToTextResponseUpdateKind.Error,
                        RawRepresentation = rawRepresentation,
                        Contents = [new TextContent(GetRequiredString(root, "message"))],
                    };
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported xAI STT streaming event type: {type}");
            }
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is not null ? null : serviceType switch
    {
        Type t when t == typeof(SpeechToTextClientMetadata) => metadata,
        Type t when t == typeof(GrokSpeechToTextClient) => this,
        Type t when t == typeof(HttpClient) => httpClient,
        Type t when t.IsInstanceOfType(this) => this,
        _ => null
    };

    /// <inheritdoc />
    public void Dispose() => httpClient.Dispose();

    static MultipartFormDataContent CreateMultipartContent(Stream audioSpeechStream, SpeechToTextOptions? options)
    {
        var content = new MultipartFormDataContent();
        var grokOptions = options as GrokSpeechToTextOptions;
        var language = GetLanguage(options);

        if (grokOptions?.Format is bool format)
        {
            if (format && language is null)
                throw new ArgumentException("xAI STT requires a language when Format is true.", nameof(options));

            content.Add(new StringContent(format ? "true" : "false"), "format");
        }

        if (language is not null)
            content.Add(new StringContent(language), "language");

        if (options?.SpeechSampleRate is int sampleRate)
            content.Add(new StringContent(sampleRate.ToString(CultureInfo.InvariantCulture)), "sample_rate");

        if (grokOptions?.AudioFormat is { Length: > 0 } audioFormat)
            content.Add(new StringContent(GetRawAudioFormat(audioFormat)), "audio_format");

        if (grokOptions?.Multichannel is bool multichannel)
            content.Add(new StringContent(multichannel ? "true" : "false"), "multichannel");

        if (grokOptions?.Channels is int channels)
            content.Add(new StringContent(channels.ToString(CultureInfo.InvariantCulture)), "channels");

        if (grokOptions?.Diarize is bool diarize)
            content.Add(new StringContent(diarize ? "true" : "false"), "diarize");

        var filename = GetFilename(audioSpeechStream);
        var streamContent = new StreamContent(audioSpeechStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetMediaType(filename));
        content.Add(streamContent, "file", filename);

        return content;
    }

    Uri GetHttpEndpoint() => GetEndpoint(endpoint, "https", "v1/stt", null);

    Uri GetStreamingEndpoint(SpeechToTextOptions? options)
    {
        var grokOptions = options as GrokSpeechToTextOptions;
        var query = new NameValueCollection
        {
            ["sample_rate"] = (options?.SpeechSampleRate ?? DefaultStreamingSampleRate).ToString(CultureInfo.InvariantCulture),
            ["encoding"] = GetStreamingEncoding(grokOptions?.AudioFormat),
        };

        if (grokOptions?.InterimResults is bool interimResults)
            query["interim_results"] = interimResults ? "true" : "false";

        if (grokOptions?.Endpointing is int endpointing)
            query["endpointing"] = endpointing.ToString(CultureInfo.InvariantCulture);

        if (GetLanguage(options) is { } language)
            query["language"] = language;

        if (grokOptions?.Diarize is bool diarize)
            query["diarize"] = diarize ? "true" : "false";

        if (grokOptions?.Multichannel is bool multichannel)
            query["multichannel"] = multichannel ? "true" : "false";

        if (grokOptions?.Channels is int channels)
            query["channels"] = channels.ToString(CultureInfo.InvariantCulture);

        return GetEndpoint(endpoint, endpoint.Scheme == Uri.UriSchemeHttp ? "ws" : "wss", "v1/stt", query);
    }

    static SpeechToTextResponse ToSpeechToTextResponse(GrokSpeechToTextResponse transcript, SpeechToTextOptions? options)
    {
        var response = new SpeechToTextResponse([new TextContent(transcript.Text ?? "")])
        {
            RawRepresentation = transcript,
            AdditionalProperties = CreateResponseAdditionalProperties(transcript),
        };

        if (transcript.Words is { Count: > 0 } words)
        {
            response.StartTime = TimeSpan.FromSeconds(words[0].Start);
            response.EndTime = TimeSpan.FromSeconds(words[^1].End);
        }
        else if (transcript.Duration is double duration)
        {
            response.StartTime = TimeSpan.Zero;
            response.EndTime = TimeSpan.FromSeconds(duration);
        }

        return response;
    }

    static SpeechToTextResponseUpdate CreateTextUpdate(
        JsonElement root,
        JsonElement rawRepresentation,
        SpeechToTextOptions? options,
        SpeechToTextResponseUpdateKind? kind = null)
    {
        var update = new SpeechToTextResponseUpdate
        {
            Kind = kind ?? (GetBoolean(root, "is_final") == true ? SpeechToTextResponseUpdateKind.TextUpdated : SpeechToTextResponseUpdateKind.TextUpdating),
            RawRepresentation = rawRepresentation,
            Contents = TryGetString(root, "text") is { } text ? [new TextContent(text)] : [],
            AdditionalProperties = CreateStreamingAdditionalProperties(root),
        };

        if (TryGetDouble(root, "start") is double start)
            update.StartTime = TimeSpan.FromSeconds(start);

        if (TryGetDouble(root, "duration") is double duration)
            update.EndTime = TimeSpan.FromSeconds((update.StartTime?.TotalSeconds ?? 0) + duration);

        return update;
    }

    static AdditionalPropertiesDictionary? CreateResponseAdditionalProperties(GrokSpeechToTextResponse transcript)
    {
        AdditionalPropertiesDictionary? properties = null;

        AddProperty(ref properties, "language", transcript.Language);
        AddProperty(ref properties, "duration", transcript.Duration);
        AddProperty(ref properties, "words", transcript.Words);
        AddProperty(ref properties, "channels", transcript.Channels);

        return properties;
    }

    static AdditionalPropertiesDictionary? CreateStreamingAdditionalProperties(JsonElement root)
    {
        AdditionalPropertiesDictionary? properties = null;

        AddProperty(ref properties, "channel_index", TryGetInt(root, "channel_index"));
        AddProperty(ref properties, "is_final", GetBoolean(root, "is_final"));
        AddProperty(ref properties, "speech_final", GetBoolean(root, "speech_final"));
        AddProperty(ref properties, "duration", TryGetDouble(root, "duration"));

        return properties;
    }

    static void AddProperty(ref AdditionalPropertiesDictionary? properties, string name, object? value)
    {
        if (value is null)
            return;

        (properties ??= [])[name] = value;
    }

    static string? GetLanguage(SpeechToTextOptions? options)
    {
        if (options?.TextLanguage is { Length: > 0 } textLanguage &&
            options.SpeechLanguage is { Length: > 0 } speechLanguage &&
            !string.Equals(textLanguage, speechLanguage, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("xAI STT does not support translation between different speech and text languages.");

        return options?.TextLanguage ?? options?.SpeechLanguage;
    }

    static string GetFilename(Stream audioSpeechStream) =>
        audioSpeechStream is FileStream fileStream ? Path.GetFileName(fileStream.Name) : DefaultFilename;

    static string GetMediaType(string filename) =>
        extensionToMediaType.TryGetValue(Path.GetExtension(filename), out var mediaType) ? mediaType : "application/octet-stream";

    static string GetRawAudioFormat(string format) => format.ToLowerInvariant() switch
    {
        "pcm" or "audio/pcm" or "audio/l16" => "pcm",
        "mulaw" or "ulaw" or "audio/basic" => "mulaw",
        "alaw" or "audio/alaw" => "alaw",
        _ => format.ToLowerInvariant(),
    };

    static string GetStreamingEncoding(string? format)
    {
        var encoding = string.IsNullOrWhiteSpace(format) ? DefaultStreamingEncoding : GetRawAudioFormat(format);

        return encoding switch
        {
            "pcm" or "mulaw" or "alaw" => encoding,
            _ => throw new ArgumentException($"Unsupported xAI STT streaming encoding: {format}", nameof(format)),
        };
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

    static async Task ThrowHttpExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body) ?
            $"xAI STT request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase})." :
            $"xAI STT request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}): {body}";

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

    static async Task SendAudioAsync(WebSocket webSocket, Stream audioSpeechStream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultStreamingChunkSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await audioSpeechStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
                    throw new InvalidOperationException($"xAI STT streaming connection closed before transcript.done: {result.CloseStatusDescription ?? result.CloseStatus?.ToString()}");

                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidOperationException($"xAI STT streaming returned unsupported message type: {result.MessageType}");

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
            throw new InvalidOperationException($"xAI STT streaming event is missing required string property '{propertyName}'.");

        return property.GetString()!;
    }

    static string? TryGetString(JsonElement json, string propertyName) =>
        json.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    static bool? GetBoolean(JsonElement json, string propertyName) =>
        json.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;

    static double? TryGetDouble(JsonElement json, string propertyName) =>
        json.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number ? property.GetDouble() : null;

    static int? TryGetInt(JsonElement json, string propertyName) =>
        json.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number ? property.GetInt32() : null;

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(GrokSpeechToTextResponse))]
    [JsonSerializable(typeof(AudioDoneMessage))]
    partial class SpeechToTextJsonContext : JsonSerializerContext { }

    sealed record GrokSpeechToTextResponse(
        string? Text,
        string? Language,
        double? Duration,
        IReadOnlyList<GrokSpeechToTextWord>? Words,
        IReadOnlyList<GrokSpeechToTextChannel>? Channels);

    sealed record GrokSpeechToTextWord(string Text, double Start, double End, int? Speaker);

    sealed record GrokSpeechToTextChannel(int Index, string Text, IReadOnlyList<GrokSpeechToTextWord>? Words);

    sealed record AudioDoneMessage
    {
        public static readonly AudioDoneMessage Instance = new();

        public string Type => "audio.done";
    }
}
