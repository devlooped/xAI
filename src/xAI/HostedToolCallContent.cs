using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Represents a hosted tool agentic call.</summary>
[Experimental("xAI001")]
public class HostedToolCallContent : AIContent
{
    /// <summary>Gets or sets the tool call ID.</summary>
    public virtual string? CallId { get; set; }
}
