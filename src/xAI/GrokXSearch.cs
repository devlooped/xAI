using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Configures Grok's agentic search tool for X.</summary>
/// <remarks>See https://docs.x.ai/docs/guides/tools/search-tools#x-search-parameters</remarks>
public class GrokXSearchTool : HostedWebSearchTool
{
    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#only-consider-x-posts-from-specific-handles</summary>
    [JsonPropertyName("allowed_x_handles")]
    public IList<string>? AllowedHandles { get; set; }
    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#exclude-x-posts-from-specific-handles</summary>
    [JsonPropertyName("excluded_x_handles")]
    public IList<string>? ExcludedHandles { get; set; }
    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#date-range</summary>
    public DateOnly? FromDate { get; set; }
    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#date-range</summary>
    public DateOnly? ToDate { get; set; }
    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#enable-image-understanding-1</summary>
    public bool EnableImageUnderstanding { get; set; }
    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#enable-video-understanding</summary>
    public bool EnableVideoUnderstanding { get; set; }
}