using System.ComponentModel;
using Grpc.Net.ClientFactory;
using xAI.Protocol;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration extensions for Grok gRPC clients.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrokServiceCollectionExtensions
{
    /// <summary>
    /// Registers Grok gRPC clients with the specified API key.
    /// </summary>
    public static IServiceCollection AddGrokClient(this IServiceCollection services, string apiKey,
        Action<GrpcClientFactoryOptions>? configureClient = null,
        Action<IHttpClientBuilder>? configureHttp = null)
    {
        var address = new Uri("https://api.x.ai/");

        var builder = services.AddGrpcClient<Auth.AuthClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Chat.ChatClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Embedder.EmbedderClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Image.ImageClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Models.ModelsClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Sample.SampleClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Tokenize.TokenizeClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        builder = services.AddGrpcClient<Documents.DocumentsClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        configureHttp?.Invoke(builder);

        return services;
    }
}
