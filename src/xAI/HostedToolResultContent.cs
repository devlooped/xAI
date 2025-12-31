using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Represents a hosted tool agentic call.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[Experimental("xAI001")]
public class HostedToolResultContent : AIContent
{
    /// <summary>Gets or sets the tool call ID.</summary>
    public virtual string? CallId { get; set; }

    /// <summary>Gets or sets the resulting contents from the tool.</summary>
    public virtual IList<AIContent>? Outputs { get; set; }
}