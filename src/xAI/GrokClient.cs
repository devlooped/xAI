using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Grpc.Net.Client;
using xAI.Protocol;

namespace xAI;

/// <summary>Client for interacting with the Grok service.</summary>
/// <param name="apiKey">The API key used for authentication.</param>
/// <param name="options">The options used to configure the client.</param>
public sealed class GrokClient(string apiKey, GrokClientOptions options) : IDisposable
{
    static readonly ConcurrentDictionary<(Uri, string), GrpcChannel> channels = [];

    /// <summary>Initializes a new instance of the <see cref="GrokClient"/> class with default options.</summary>
    public GrokClient(string apiKey) : this(apiKey, new GrokClientOptions()) { }

    /// <summary>Gets the API key used for authentication.</summary>
    public string ApiKey { get; } = apiKey;

    /// <summary>Gets or sets the endpoint for the service.</summary>
    public Uri Endpoint { get; set; } = options.Endpoint;

    /// <summary>Gets the options used to configure the client.</summary>
    public GrokClientOptions Options { get; } = options;

    /// <summary>Gets a new instance of <see cref="Auth.AuthClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Auth.AuthClient GetAuthClient() => new(Channel);

    /// <summary>Gets a new instance of <see cref="Chat.ChatClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Chat.ChatClient GetChatClient() => new(Channel);

    /// <summary>Gets a new instance of <see cref="Documents.DocumentsClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Documents.DocumentsClient GetDocumentsClient() => new(Channel);

    /// <summary>Gets a new instance of <see cref="Embedder.EmbedderClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Embedder.EmbedderClient GetEmbedderClient() => new(Channel);

    /// <summary>Gets a new instance of <see cref="Image.ImageClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Image.ImageClient GetImageClient() => new(Channel);

    /// <summary>Gets a new instance of <see cref="Models.ModelsClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Models.ModelsClient GetModelsClient() => new(Channel);

    /// <summary>Gets a new instance of <see cref="Tokenize.TokenizeClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Tokenize.TokenizeClient GetTokenizeClient() => new(Channel);

    internal GrpcChannel Channel => channels.GetOrAdd((Endpoint, ApiKey), key =>
    {
        var handler = new AuthenticationHeaderHandler(ApiKey)
        {
            InnerHandler = Options.ChannelOptions?.HttpHandler ?? new HttpClientHandler()
        };

        var options = Options.ChannelOptions ?? new GrpcChannelOptions();
        options.HttpHandler = handler;

        return GrpcChannel.ForAddress(Endpoint, options);
    });

    /// <summary>Clears the cached list of gRPC channels in the client.</summary>
    public void Dispose() => channels.Clear();

    class AuthenticationHeaderHandler(string apiKey) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
