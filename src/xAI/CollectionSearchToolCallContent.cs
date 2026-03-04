using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Represents a hosted collection search call.</summary>
public class CollectionSearchToolCallContent : AIContent
{
    /// <summary>Gets or sets the tool call ID.</summary>
    public virtual string? CallId { get; set; }
}
