using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Configures Grok's agentic search tool.</summary>
/// <remarks>See https://docs.x.ai/docs/guides/tools/search-tools</remarks>
public class GrokSearchTool : HostedWebSearchTool
{
    /// <inheritdoc/>
    public override string Name => "web_search";

    /// <inheritdoc/>
    public override string Description => "Performs agentic web search";

    /// <summary>Use to make the web search only perform the search and web browsing on web pages that fall within the specified domains. Can include a maximum of five domains.</summary>
    public IList<string>? AllowedDomains { get; set; }

    /// <summary>Use to prevent the model from including the specified domains in any web search tool invocations and from browsing any pages on those domains. Can include a maximum of five domains.</summary>
    public IList<string>? ExcludedDomains { get; set; }

    /// <summary>See https://docs.x.ai/docs/guides/tools/search-tools#enable-image-understanding</summary>
    public bool EnableImageUnderstanding { get; set; }

    /// <summary>Sets the user's country for web search results, using the ISO alpha-2 code.</summary>
    public string? Country { get; set; }

    /// <summary>Additional free text information about the region to be used in the search.</summary>
    public string? Region { get; set; }

    /// <summary>Additional free text information about the city to be used in the search.</summary>
    public string? City { get; set; }

    /// <summary>IANA timezone name to be used in the search.</summary>
    public string? Timezone { get; set; }
}