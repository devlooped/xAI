using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.AI;
using xAI.Protocol;
using static xAI.Protocol.Image;

namespace xAI;

/// <summary>
/// Represents an <see cref="IImageGenerator"/> for xAI's Grok image generation service.
/// </summary>
sealed class GrokImageGenerator : IImageGenerator
{
    readonly ImageGeneratorMetadata metadata;
    readonly ImageClient imageClient;
    readonly GrokClientOptions clientOptions;
    readonly string defaultModelId;

    internal GrokImageGenerator(ChannelBase channel, GrokClientOptions options, string defaultModelId)
        : this(new ImageClient(channel, options), options, defaultModelId)
    { }

    /// <summary>
    /// Test constructor.
    /// </summary>
    internal GrokImageGenerator(ImageClient imageClient, string defaultModelId)
        : this(imageClient, imageClient.Options as GrokClientOptions ?? new(), defaultModelId)
    { }

    GrokImageGenerator(ImageClient imageClient, GrokClientOptions clientOptions, string defaultModelId)
    {
        this.imageClient = imageClient;
        this.clientOptions = clientOptions;
        this.defaultModelId = defaultModelId;
        metadata = new ImageGeneratorMetadata("xai", clientOptions.Endpoint, defaultModelId);
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var protocolRequest = request.AsProtocolImageRequest(options, defaultModelId, clientOptions.EndUserId);
        var response = await imageClient.GenerateImageAsync(protocolRequest, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.AsImageGenerationResponse();
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType switch
    {
        Type t when t == typeof(ImageGeneratorMetadata) => metadata,
        Type t when t == typeof(GrokImageGenerator) => this,
        _ => null
    };

    /// <inheritdoc />
    void IDisposable.Dispose() { }

}
