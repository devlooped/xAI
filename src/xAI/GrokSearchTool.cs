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
}