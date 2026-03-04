using Microsoft.Extensions.AI;
using xAI.Protocol;

namespace xAI;

/// <summary>Grok-specific image generation options that extend the base <see cref="ImageGenerationOptions"/>.</summary>
/// <remarks>
/// These options map to image.proto fields and are only supported by grok-imagine models.
/// If not specified, the API defaults to 1:1 aspect ratio and 1k resolution.
/// </remarks>
public class GrokImageGenerationOptions : ImageGenerationOptions
{
    /// <summary>Optional aspect ratio for image generation and editing.</summary>
    /// <remarks>
    /// Proto default is 1:1 when this option is not specified.
    /// Auto aspect ratio is only supported for image generation with a thinking upsampler.
    /// This option is only supported by grok-imagine models.
    /// </remarks>
    public ImageAspectRatio? AspectRatio { get; set; }

    /// <summary>Optional resolution for image generation and editing.</summary>
    /// <remarks>
    /// Proto default is 1k when this option is not specified.
    /// 2k output is generated at 1k and then upscaled with super-resolution.
    /// This option is only supported by grok-imagine models.
    /// </remarks>
    public ImageResolution? Resolution { get; set; }
}
