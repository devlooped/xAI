using System.ComponentModel;
using Microsoft.Extensions.AI;
using xAI.Protocol;

namespace xAI;

/// <summary>Customizes Grok's agentic search tools.</summary>
/// <remarks>See https://docs.x.ai/docs/guides/tools/search-tools.</remarks>
[Flags]
public enum GrokSearch
{
    /// <summary>Disables agentic search capabilities.</summary>
    None = 0,
    /// <summary>Enables all available agentic search capabilities.</summary>
    All = Web | X,
    /// <summary>Allows the agent to search the web and browse pages.</summary>
    Web = 1,
    /// <summary>Allows the agent to perform keyword search, semantic search, user search, and thread fetch on X.</summary>
    X = 2,
}

/// <summary>Grok-specific chat options that extend the base <see cref="ChatOptions"/>.</summary>
public class GrokChatOptions : ChatOptions
{
    /// <summary>Configures Grok's agentic search capabilities.</summary>
    /// <remarks>See https://docs.x.ai/docs/guides/tools/search-tools.</remarks>
    public GrokSearch Search { get; set; } = GrokSearch.None;

    /// <summary>Additional outputs to include in responses.</summary>
    /// <remarks>Defaults to including <see cref="IncludeOption.InlineCitations"/>.</remarks>
    public IList<IncludeOption> Include { get; set; } = [IncludeOption.InlineCitations];
}
