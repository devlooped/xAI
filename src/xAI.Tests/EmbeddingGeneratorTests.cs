using Microsoft.Extensions.AI;
using Moq;
using Tests.Client.Helpers;
using xAI;
using xAI.Protocol;
using static ConfigurationExtensions;

namespace xAI.Tests;

public class EmbeddingGeneratorTests(ITestOutputHelper output)
{
    [SecretsFact("XAI_API_KEY")]
    public async Task GrokGeneratesEmbeddings()
    {
        var client = new GrokClient(Configuration["XAI_API_KEY"]!);
        var generator = client.AsIEmbeddingGenerator("v1");

        var response = await generator.GenerateAsync(["Hello, world!", "How are you?"]);

        Assert.NotNull(response);
        Assert.Equal(2, response.Count);

        foreach (var embedding in response)
        {
            Assert.NotNull(embedding.ModelId);
            Assert.NotNull(embedding.CreatedAt);
            Assert.NotEmpty(embedding.Vector.ToArray());
        }
    }

    [Fact]
    public void AsIEmbeddingGenerator_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>("client", () => ((GrokClient)null!).AsIEmbeddingGenerator("model"));
    }

    [Fact]
    public void AsIEmbeddingGenerator_ProducesExpectedMetadata()
    {
        Uri endpoint = new("https://api.x.ai");
        string model = "v1";

        var clientOptions = new GrokClientOptions { Endpoint = endpoint };
        var mockClient = new Mock<xAI.Protocol.Embedder.EmbedderClient>(MockBehavior.Strict);

        var embeddingGenerator = new GrokEmbeddingGenerator(mockClient.Object, model);
        var metadata = embeddingGenerator.GetService<EmbeddingGeneratorMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("xai", metadata.ProviderName);
        Assert.Equal(model, metadata.DefaultModelId);
    }

    [Fact]
    public void GetService_SuccessfullyReturnsUnderlyingClient()
    {
        var mockClient = new Mock<xAI.Protocol.Embedder.EmbedderClient>(MockBehavior.Strict);
        var embeddingGenerator = new GrokEmbeddingGenerator(mockClient.Object, "model");

        Assert.Same(embeddingGenerator, embeddingGenerator.GetService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.Same(mockClient.Object, embeddingGenerator.GetService<xAI.Protocol.Embedder.EmbedderClient>());
        Assert.Same(embeddingGenerator, embeddingGenerator.GetService<GrokEmbeddingGenerator>());
    }

    [Fact]
    public async Task GenerateAsync_ExpectedRequestResponse()
    {
        var mockClient = new Mock<xAI.Protocol.Embedder.EmbedderClient>(MockBehavior.Strict);

        var response = new EmbedResponse
        {
            Id = "test-id",
            Model = "v1",
            SystemFingerprint = "test-fingerprint",
            Usage = new EmbeddingUsage
            {
                NumTextEmbeddings = 2,
                NumImageEmbeddings = 0
            }
        };

        // Add first embedding
        var embedding1 = new xAI.Protocol.Embedding { Index = 0 };
        var featureVector1 = new FeatureVector();
        featureVector1.FloatArray.AddRange([0.1f, 0.2f, 0.3f]);
        embedding1.Embeddings.Add(featureVector1);
        response.Embeddings.Add(embedding1);

        // Add second embedding
        var embedding2 = new xAI.Protocol.Embedding { Index = 1 };
        var featureVector2 = new FeatureVector();
        featureVector2.FloatArray.AddRange([0.4f, 0.5f, 0.6f]);
        embedding2.Embeddings.Add(featureVector2);
        response.Embeddings.Add(embedding2);

        mockClient
            .Setup(x => x.EmbedAsync(It.IsAny<EmbedRequest>(), null, null, CancellationToken.None))
            .Returns(CallHelpers.CreateAsyncUnaryCall(response));

        var embeddingGenerator = new GrokEmbeddingGenerator(mockClient.Object, "v1");

        var result = await embeddingGenerator.GenerateAsync(["hello, world!", "how are you?"]);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        Assert.NotNull(result.Usage);
        Assert.Equal(2, result.Usage.InputTokenCount);
        Assert.Equal(2, result.Usage.TotalTokenCount);

        var first = result[0];
        Assert.Equal("v1", first.ModelId);
        Assert.NotNull(first.CreatedAt);
        Assert.Equal(3, first.Vector.Length);
        Assert.Equal([0.1f, 0.2f, 0.3f], first.Vector.ToArray());

        var second = result[1];
        Assert.Equal("v1", second.ModelId);
        Assert.NotNull(second.CreatedAt);
        Assert.Equal(3, second.Vector.Length);
        Assert.Equal([0.4f, 0.5f, 0.6f], second.Vector.ToArray());
    }

    [Fact]
    public async Task GenerateAsync_MissingUsage_ReturnsNullUsage()
    {
        var mockClient = new Mock<xAI.Protocol.Embedder.EmbedderClient>(MockBehavior.Strict);

        var response = new EmbedResponse
        {
            Id = "test-id",
            Model = "v1",
        };

        // Add embedding without usage
        var embedding = new xAI.Protocol.Embedding { Index = 0 };
        var featureVector = new FeatureVector();
        featureVector.FloatArray.AddRange([0.1f, 0.2f, 0.3f]);
        embedding.Embeddings.Add(featureVector);
        response.Embeddings.Add(embedding);

        mockClient
            .Setup(x => x.EmbedAsync(It.IsAny<EmbedRequest>(), null, null, CancellationToken.None))
            .Returns(CallHelpers.CreateAsyncUnaryCall(response));

        var embeddingGenerator = new GrokEmbeddingGenerator(mockClient.Object, "v1");

        var result = await embeddingGenerator.GenerateAsync(["hello, world!"]);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Null(result.Usage);

        var first = result[0];
        Assert.Equal("v1", first.ModelId);
        Assert.NotNull(first.CreatedAt);
        Assert.Equal(3, first.Vector.Length);
    }

    [Fact]
    public async Task GenerateAsync_UsesCustomModelId()
    {
        var mockClient = new Mock<xAI.Protocol.Embedder.EmbedderClient>(MockBehavior.Strict);
        EmbedRequest? capturedRequest = null;

        var response = new EmbedResponse
        {
            Id = "test-id",
            Model = "custom-model",
        };

        var embedding = new xAI.Protocol.Embedding { Index = 0 };
        var featureVector = new FeatureVector();
        featureVector.FloatArray.AddRange([0.1f, 0.2f, 0.3f]);
        embedding.Embeddings.Add(featureVector);
        response.Embeddings.Add(embedding);

        mockClient
            .Setup(x => x.EmbedAsync(It.IsAny<EmbedRequest>(), null, null, CancellationToken.None))
            .Callback<EmbedRequest, Grpc.Core.Metadata?, DateTime?, CancellationToken>((req, _, _, _) => capturedRequest = req)
            .Returns(CallHelpers.CreateAsyncUnaryCall(response));

        var embeddingGenerator = new GrokEmbeddingGenerator(mockClient.Object, "default-model");

        var result = await embeddingGenerator.GenerateAsync(
            ["hello"],
            new EmbeddingGenerationOptions { ModelId = "custom-model" });

        Assert.NotNull(capturedRequest);
        Assert.Equal("custom-model", capturedRequest.Model);
    }
}
