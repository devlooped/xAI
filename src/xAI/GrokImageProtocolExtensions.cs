using Microsoft.Extensions.AI;
using xAI.Protocol;

namespace xAI;

public static partial class GrokProtocolExtensions
{
    static readonly Dictionary<string, string> imageExtensionToMimeType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tiff"] = "image/tiff",
    };

    internal static GenerateImageRequest AsProtocolImageRequest(
        this ImageGenerationRequest request,
        ImageGenerationOptions? options,
        string defaultModelId,
        string? endUserId)
    {
        Throw.IfNull(request);

        var protocolRequest = new GenerateImageRequest
        {
            Prompt = Throw.IfNull(request.Prompt, "request.Prompt"),
            Model = options?.ModelId ?? defaultModelId,
        };

        if (endUserId is not null)
            protocolRequest.User = endUserId;

        if (options?.Count is { } count)
            protocolRequest.N = count;

        protocolRequest.Format = (options?.ResponseFormat ?? ImageGenerationResponseFormat.Uri) switch
        {
            ImageGenerationResponseFormat.Uri => ImageFormat.ImgFormatUrl,
            ImageGenerationResponseFormat.Data => ImageFormat.ImgFormatBase64,
            _ => throw new ArgumentException($"Unsupported response format: {options?.ResponseFormat}", nameof(options))
        };

        if (options is GrokImageGenerationOptions grokOptions)
        {
            if (grokOptions.AspectRatio is { } aspectRatio)
                protocolRequest.AspectRatio = aspectRatio;
            if (grokOptions.Resolution is { } resolution)
                protocolRequest.Resolution = resolution;
            if (grokOptions.Quality is { } quality)
                protocolRequest.Quality = quality;

            if (grokOptions.Storage is { } storage)
            {
                protocolRequest.StorageOptions = new()
                {
                    Filename = storage.Filename ?? "",
                };

                if (storage.ExpiresAfterSeconds is long fileExpiry)
                    protocolRequest.StorageOptions.ExpiresAfter = fileExpiry;

                if (storage.CreatePublicUrl || storage.PublicUrlExpiresAfterSeconds is not null)
                {
                    protocolRequest.StorageOptions.PublicUrl = new();
                    if (storage.PublicUrlExpiresAfterSeconds is long urlExpiry)
                        protocolRequest.StorageOptions.PublicUrl.ExpiresAfter = urlExpiry;
                }
            }
        }

        if (request.OriginalImages?.ToList() is { Count: > 0 } originalImages)
        {
            if (originalImages.Count == 1)
            {
                if (MapToImageUrlContent(originalImages[0]) is { } image)
                    protocolRequest.Image = image;
            }
            else
            {
                foreach (var originalImage in originalImages)
                    if (MapToImageUrlContent(originalImage) is { } image)
                        protocolRequest.Images.Add(image);
            }
        }

        return protocolRequest;
    }

    internal static ImageGenerationResponse AsImageGenerationResponse(this ImageResponse response)
    {
        var contents = new List<AIContent>();

        foreach (var image in response.Images)
        {
            AIContent content = image.ImageCase switch
            {
                GeneratedImage.ImageOneofCase.Base64 => CreateGeneratedImageDataContent(image.Base64),
                GeneratedImage.ImageOneofCase.Url => new UriContent(
                    new Uri(image.Url),
                    Path.GetExtension(image.Url) is { } extension && imageExtensionToMimeType.TryGetValue(extension, out var mimeType) ? mimeType : "image/jpeg"),
                _ => throw new InvalidOperationException("Generated image does not contain a valid URL or base64 data."),
            };

            content.RawRepresentation = image;
            content.AdditionalProperties = new()
            {
                ["respect_moderation"] = image.RespectModeration,
            };

            if (image.FileOutput is not null)
                content.AdditionalProperties["file_output"] = image.FileOutput;
            if (!string.IsNullOrEmpty(image.StorageError))
                content.AdditionalProperties["storage_error"] = image.StorageError;

            contents.Add(content);
        }

        return new ImageGenerationResponse(contents)
        {
            RawRepresentation = response,
            Usage = response.Usage?.Convert(),
        };
    }

    static DataContent CreateGeneratedImageDataContent(string imageData)
    {
        try
        {
            // New Imagine responses may use a data URI; older models return raw base64.
            return new DataContent(imageData);
        }
        catch (FormatException)
        {
            return new DataContent(System.Convert.FromBase64String(imageData), "image/jpeg");
        }
    }

    static ImageUrlContent? MapToImageUrlContent(AIContent content) => content switch
    {
        DataContent dataContent => MapToImageUrlContent(dataContent),
        UriContent uriContent => new ImageUrlContent { ImageUrl = uriContent.Uri.ToString() },
        _ => throw new ArgumentException($"Unsupported original image content type: {content.GetType()}", nameof(content)),
    };

    static ImageUrlContent? MapToImageUrlContent(DataContent dataContent)
    {
        var imageUrl = dataContent.Uri?.ToString();
        if (imageUrl is null && dataContent.Data.Length > 0)
            imageUrl = $"data:{dataContent.MediaType ?? "image/png"};base64,{System.Convert.ToBase64String(dataContent.Data.ToArray())}";

        return imageUrl is null ? null : new ImageUrlContent { ImageUrl = imageUrl };
    }
}
