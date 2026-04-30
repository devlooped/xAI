using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using xAI.Protocol;

namespace xAI;

/// <summary>Client for interacting with the Grok service.</summary>
/// <param name="apiKey">The API key used for authentication.</param>
/// <param name="options">The options used to configure the client.</param>
public sealed class GrokClient(string apiKey, GrokClientOptions options) : IDisposable
{
    static readonly ConcurrentDictionary<(Uri, string), (ChannelBase, HttpMessageHandler)> channels = [];

    /// <summary>Initializes a new instance of the <see cref="GrokClient"/> class with default options.</summary>
    public GrokClient(string apiKey) : this(apiKey, new GrokClientOptions()) { }

    /// <summary>Testing ctor.</summary>
    internal GrokClient(ChannelBase channel, GrokClientOptions options, string? apiKey = default) : this(apiKey ?? "", options)
        => channels[(options.Endpoint, apiKey ?? "")] = (channel, GetHttpHandler(options.ChannelOptions, apiKey ?? ""));

    /// <summary>Gets the API key used for authentication.</summary>
    public string ApiKey { get; } = apiKey;

    /// <summary>Gets or sets the endpoint for the service.</summary>
    public Uri Endpoint { get; set; } = options.Endpoint;

    /// <summary>Gets the options used to configure the client.</summary>
    public GrokClientOptions Options { get; } = options;

    /// <summary>Gets a new instance of <see cref="Auth.AuthClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Auth.AuthClient GetAuthClient() => new(ChannelHandler.Channel);

    /// <summary>Gets a new instance of <see cref="Chat.ChatClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Chat.ChatClient GetChatClient() => new(ChannelHandler.Channel, Options);

    /// <summary>Gets a new instance of <see cref="Documents.DocumentsClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Documents.DocumentsClient GetDocumentsClient() => new(ChannelHandler.Channel);

    /// <summary>Gets a new instance of <see cref="Embedder.EmbedderClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Embedder.EmbedderClient GetEmbedderClient() => new(ChannelHandler.Channel);

    /// <summary>Gets a new instance of <see cref="Image.ImageClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Image.ImageClient GetImageClient() => new(ChannelHandler.Channel, Options);

    /// <summary>Gets a new instance of <see cref="Models.ModelsClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Models.ModelsClient GetModelsClient() => new(ChannelHandler.Channel);

    /// <summary>Gets a new instance of <see cref="Tokenize.TokenizeClient"/> that reuses the client configuration details provided to the <see cref="GrokClient"/> instance.</summary>
    public Tokenize.TokenizeClient GetTokenizeClient() => new(ChannelHandler.Channel);

    internal (ChannelBase Channel, HttpMessageHandler Handler) ChannelHandler => channels.GetOrAdd((Endpoint, ApiKey), key =>
    {
        var handler = GetHttpHandler(Options.ChannelOptions, key.Item2);

        // Provide some sensible defaults for gRPC channel options, while allowing users to
        // override them via GrokClientOptions.ChannelOptions if needed.
        var options = Options.ChannelOptions ?? new GrpcChannelOptions
        {
            DisposeHttpClient = true,
            MaxReceiveMessageSize = 128 * 1024 * 1024,   // large enough for tool output
            MaxSendMessageSize = 16 * 1024 * 1024,
        };

        options.HttpHandler = handler;

        return (GrpcChannel.ForAddress(key.Item1, options), handler);
    });

    static HttpMessageHandler GetHttpHandler(GrpcChannelOptions? options, string apiKey)
    {
        var inner = options?.HttpHandler;
        if (inner == null)
        {
            // If no custom HttpHandler is provided, we create one with Polly retry
            // policies to handle transient errors, including gRPC-specific ones.
            var retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<Grpc.Core.RpcException>(ex =>
                    ex.StatusCode is StatusCode.Unavailable or
                                     StatusCode.DeadlineExceeded or
                                     StatusCode.Internal &&
                    ex.Status.Detail?.Contains("504") == true ||
                    ex.Status.Detail?.Contains("INTERNAL_ERROR") == true)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
#if DEBUG
                    , onRetry: (outcome, delay, retryCount, ctx) =>
                    {
                        Debug.WriteLine($"[xAI Streaming Retry #{retryCount}] {outcome.Exception?.Message} — waiting {delay.TotalSeconds}s");
                    }
#endif
                    );

            inner = new PolicyHttpMessageHandler(retryPolicy)
            {
                InnerHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(20),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,   // crucial for long streams
                    EnableMultipleHttp2Connections = true,
                    ConnectTimeout = TimeSpan.FromSeconds(60),
                    MaxConnectionsPerServer = 10
                }
            };
        }

        var handler = string.IsNullOrEmpty(apiKey) ? inner : new AuthenticationHeaderHandler(apiKey)
        {
            InnerHandler = inner
        };

        return handler;
    }

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
