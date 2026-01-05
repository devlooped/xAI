using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Extensions.AI;
using xAI.Protocol;
using static xAI.Protocol.Image;

namespace xAI;

/// <summary>
/// Represents an <see cref="IImageGenerator"/> for xAI's Grok image generation service.
/// </summary>
internal sealed class GrokImageGenerator : IImageGenerator
{
    /// <summary>Metadata about the image generator.</summary>
    private readonly ImageGeneratorMetadata _metadata;

    /// <summary>The underlying <see cref="ImageClient"/>.</summary>
    private readonly ImageClient _imageClient;

    /// <summary>The default model ID to use for image generation.</summary>
    private readonly string _defaultModelId;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrokImageGenerator"/> class for the specified <see cref="GrpcChannel"/>.
    /// </summary>
    /// <param name="channel">The gRPC channel to use for communication.</param>
    /// <param name="defaultModelId">The default model ID to use for image generation.</param>
    internal GrokImageGenerator(GrpcChannel channel, string defaultModelId)
        : this(new ImageClient(channel), defaultModelId)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GrokImageGenerator"/> class for the specified <see cref="ImageClient"/>.
    /// </summary>
    /// <param name="imageClient">The underlying image client.</param>
    /// <param name="defaultModelId">The default model ID to use for image generation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="imageClient"/> is <see langword="null"/>.</exception>
    public GrokImageGenerator(ImageClient imageClient, string defaultModelId)
    {
        _imageClient = Throw.IfNull(imageClient);
        _defaultModelId = Throw.IfNullOrWhitespace(defaultModelId);
        _metadata = new ImageGeneratorMetadata("xai", null, defaultModelId);
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(request);

        string? prompt = request.Prompt;
        _ = Throw.IfNull(prompt);

        // Build the protocol request
        var protocolRequest = new GenerateImageRequest
        {
            Prompt = prompt,
            Model = options?.ModelId ?? _defaultModelId,
        };

        // Set the number of images to generate
        if (options?.Count is { } count)
        {
            protocolRequest.N = count;
        }

        // Set the response format (URL or base64)
        if (options?.ResponseFormat is { } responseFormat)
        {
            protocolRequest.Format = responseFormat switch
            {
                ImageGenerationResponseFormat.Uri => ImageFormat.ImgFormatUrl,
                ImageGenerationResponseFormat.Data => ImageFormat.ImgFormatBase64,
                _ => ImageFormat.ImgFormatInvalid
            };
        }

        // Handle image editing if original images are provided
        if (request.OriginalImages is not null && request.OriginalImages.Any())
        {
            var originalImage = request.OriginalImages.FirstOrDefault();
            if (originalImage is DataContent dataContent)
            {
                // Convert the data content to a base64 string or URL for the API
                var imageUrl = dataContent.Uri?.ToString();
                if (imageUrl == null && dataContent.Data.Length > 0)
                {
                    // Convert to base64 if we have raw data
                    imageUrl = $"data:{dataContent.MediaType ?? "image/png"};base64,{Convert.ToBase64String(dataContent.Data.ToArray())}";
                }

                if (imageUrl != null)
                {
                    protocolRequest.Image = new ImageUrlContent
                    {
                        ImageUrl = imageUrl
                    };
                }
            }
            else if (originalImage is UriContent uriContent)
            {
                protocolRequest.Image = new ImageUrlContent
                {
                    ImageUrl = uriContent.Uri.ToString()
                };
            }
        }

        // Call the gRPC API
        var response = await _imageClient.GenerateImageAsync(protocolRequest, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Convert the response to the Microsoft.Extensions.AI format
        return ToImageGenerationResponse(response, options?.MediaType);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType is null ? throw new ArgumentNullException(nameof(serviceType)) :
        serviceKey is not null ? null :
        serviceType == typeof(ImageGeneratorMetadata) ? _metadata :
        serviceType == typeof(ImageClient) ? _imageClient :
        serviceType.IsInstanceOfType(this) ? this :
        null;

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        // Nothing to dispose. Implementation required for the IImageGenerator interface.
    }

    /// <summary>
    /// Converts an xAI <see cref="ImageResponse"/> to a <see cref="ImageGenerationResponse"/>.
    /// </summary>
    private static ImageGenerationResponse ToImageGenerationResponse(ImageResponse response, string? mediaType)
    {
        var contents = new List<AIContent>();
        var contentType = mediaType ?? "image/jpeg"; // xAI returns JPG by default

        foreach (var image in response.Images)
        {
            switch (image.ImageCase)
            {
                case GeneratedImage.ImageOneofCase.Base64:
                    {
                        var imageBytes = Convert.FromBase64String(image.Base64);
                        contents.Add(new DataContent(imageBytes, contentType));
                        break;
                    }
                case GeneratedImage.ImageOneofCase.Url:
                    {
                        contents.Add(new UriContent(new Uri(image.Url), contentType));
                        break;
                    }
                default:
                    throw new InvalidOperationException("Generated image does not contain a valid URL or base64 data.");
            }
        }

        return new ImageGenerationResponse(contents)
        {
            ModelId = response.Model,
            RawRepresentation = response,
        };
    }
}
