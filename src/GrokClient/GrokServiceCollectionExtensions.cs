using System;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;

namespace Devlooped.Grok;

public static class GrokServiceCollectionExtensions
{
    public static IServiceCollection AddGrokClient(this IServiceCollection services, string apiKey, 
        Action<GrpcClientFactoryOptions>? configureClient = null, 
        Action<IHttpClientBuilder>? configureHttp = null)
    {
        var address = new Uri("https://api.x.ai/");

        Action<HttpClient> http = client => client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        services.AddGrpcClient<Auth.AuthClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        services.AddGrpcClient<Chat.ChatClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        services.AddGrpcClient<Embedder.EmbedderClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        services.AddGrpcClient<Image.ImageClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        var builder = services.AddGrpcClient<Models.ModelsClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        }).ConfigureHttpClient(http);

        configureHttp?.Invoke(builder);

        //.AddCallCredentials((context, metadata) =>
        //{
        //    metadata.Add("Authorization", $"Bearer {apiKey}");
        //    return Task.CompletedTask;
        //});

        services.AddGrpcClient<Sample.SampleClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        services.AddGrpcClient<Tokenize.TokenizeClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        services.AddGrpcClient<Documents.DocumentsClient>(options =>
        {
            options.Address = address;
            configureClient?.Invoke(options);
        })
        .AddCallCredentials((context, metadata) =>
        {
            metadata.Add("Authorization", $"Bearer {apiKey}");
            return Task.CompletedTask;
        });

        return services;
    }
}
