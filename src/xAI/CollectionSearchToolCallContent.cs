using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Represents a hosted tool agentic call.</summary>
public class CollectionSearchToolCallContent : AIContent
{
    /// <summary>Gets or sets the tool call ID.</summary>
    public virtual string? CallId { get; set; }
}
