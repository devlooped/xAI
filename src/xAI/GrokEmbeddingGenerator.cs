using Grpc.Net.Client;
using Microsoft.Extensions.AI;
using xAI.Protocol;
using static xAI.Protocol.Embedder;

namespace xAI;

class GrokEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    readonly EmbeddingGeneratorMetadata metadata;
    readonly EmbedderClient client;
    readonly string defaultModelId;
    readonly GrokClientOptions clientOptions;

    internal GrokEmbeddingGenerator(GrpcChannel channel, GrokClientOptions clientOptions, string defaultModelId)
        : this(new EmbedderClient(channel), clientOptions, defaultModelId)
    { }

    /// <summary>
    /// Test constructor.
    /// </summary>
    internal GrokEmbeddingGenerator(EmbedderClient client, string defaultModelId)
        : this(client, new(), defaultModelId)
    { }

    GrokEmbeddingGenerator(EmbedderClient client, GrokClientOptions clientOptions, string defaultModelId)
    {
        this.client = client;
        this.clientOptions = clientOptions;
        this.defaultModelId = defaultModelId;
        metadata = new EmbeddingGeneratorMetadata("xai", clientOptions.Endpoint, defaultModelId);
    }

    /// <inheritdoc />
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = new EmbedRequest
        {
            Model = options?.ModelId ?? defaultModelId,
            EncodingFormat = EmbedEncodingFormat.FormatFloat
        };

        foreach (var value in values)
        {
            request.Input.Add(new EmbedInput { String = value });
        }

        if ((clientOptions.EndUserId) is { } user)
            request.User = user;

        var response = await client.EmbedAsync(request, cancellationToken: cancellationToken);

        var result = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var embedding in response.Embeddings.OrderBy(e => e.Index))
        {
            // Each input can produce multiple feature vectors, we take the first one for text inputs
            var featureVector = embedding.Embeddings.FirstOrDefault();
            if (featureVector != null)
            {
                result.Add(new Embedding<float>(featureVector.FloatArray.ToArray())
                {
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModelId = response.Model,
                });
            }
        }

        if (response.Usage != null)
        {
            result.Usage = new UsageDetails
            {
                InputTokenCount = response.Usage.NumTextEmbeddings,
                TotalTokenCount = response.Usage.NumTextEmbeddings + response.Usage.NumImageEmbeddings
            };
        }

        return result;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType switch
    {
        Type t when t == typeof(EmbeddingGeneratorMetadata) => metadata,
        Type t when t == typeof(GrokEmbeddingGenerator) => this,
        Type t when t == typeof(EmbedderClient) => client,
        Type t when t.IsInstanceOfType(this) => this,
        _ => null
    };

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose. Implementation required for the IEmbeddingGenerator interface.
    }
}
