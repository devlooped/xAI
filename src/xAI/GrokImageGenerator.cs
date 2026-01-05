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
    // add inverted dictionary for extension to mime type if needed in future
    static readonly Dictionary<string, string> extensionToMimeType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tiff"] = "image/tiff",
    };

    const string DefaultInputContentType = "image/png";
    const string DefaultOutputContentType = "image/jpeg";

    readonly ImageGeneratorMetadata metadata;
    readonly ImageClient imageClient;
    readonly string defaultModelId;

    internal GrokImageGenerator(GrpcChannel channel, GrokClientOptions clientOptions, string defaultModelId)
        : this(new ImageClient(channel), clientOptions, defaultModelId)
    { }

    /// <summary>
    /// Test constructor.
    /// </summary>
    internal GrokImageGenerator(ImageClient imageClient, string defaultModelId)
        : this(imageClient, new(), defaultModelId)
    { }

    GrokImageGenerator(ImageClient imageClient, GrokClientOptions clientOptions, string defaultModelId)
    {
        this.imageClient = imageClient;
        this.defaultModelId = defaultModelId;
        metadata = new ImageGeneratorMetadata("xai", clientOptions.Endpoint, defaultModelId);
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var protocolRequest = new GenerateImageRequest
        {
            Prompt = Throw.IfNull(Throw.IfNull(request).Prompt, "request.Prompt"),
            Model = options?.ModelId ?? defaultModelId,
        };

        if (options?.Count is { } count)
            protocolRequest.N = count;

        protocolRequest.Format = (options?.ResponseFormat ?? ImageGenerationResponseFormat.Uri) switch
        {
            ImageGenerationResponseFormat.Uri => ImageFormat.ImgFormatUrl,
            ImageGenerationResponseFormat.Data => ImageFormat.ImgFormatBase64,
            _ => throw new ArgumentException($"Unsupported response format: {options?.ResponseFormat}", nameof(options))
        };

        // Handle image editing if original images are provided
        if (request.OriginalImages?.FirstOrDefault() is { } originalImage)
        {
            if (originalImage is DataContent dataContent)
            {
                var imageUrl = dataContent.Uri?.ToString();
                if (imageUrl == null && dataContent.Data.Length > 0)
                    imageUrl = $"data:{dataContent.MediaType ?? DefaultInputContentType};base64,{Convert.ToBase64String(dataContent.Data.ToArray())}";

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

        var response = await imageClient.GenerateImageAsync(protocolRequest, cancellationToken: cancellationToken).ConfigureAwait(false);

        return ToImageGenerationResponse(response);
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

    /// <summary>
    /// Converts an xAI <see cref="ImageResponse"/> to a <see cref="ImageGenerationResponse"/>.
    /// </summary>
    static ImageGenerationResponse ToImageGenerationResponse(ImageResponse response)
    {
        var contents = new List<AIContent>();
        var contentType = DefaultOutputContentType;

        foreach (var image in response.Images)
        {
            switch (image.ImageCase)
            {
                case GeneratedImage.ImageOneofCase.Base64:
                    {
                        // We assume JPEG since there's no way to get the actual content type.
                        var imageBytes = Convert.FromBase64String(image.Base64);
                        contents.Add(new DataContent(imageBytes, contentType));
                        break;
                    }
                case GeneratedImage.ImageOneofCase.Url:
                    {
                        if (Path.GetExtension(image.Url) is { } extension && extensionToMimeType.TryGetValue(extension, out var mimeType))
                            contentType = mimeType;

                        contents.Add(new UriContent(new Uri(image.Url), contentType));
                        break;
                    }
                default:
                    throw new InvalidOperationException("Generated image does not contain a valid URL or base64 data.");
            }
        }

        return new ImageGenerationResponse(contents)
        {
            RawRepresentation = response,
        };
    }
}
