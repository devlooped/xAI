using Grpc.Net.Client;

namespace xAI;

/// <summary>Options for configuring the <see cref="GrokClient"/>.</summary>
public class GrokClientOptions
{
    /// <summary> Gets or sets the service endpoint. </summary>
    public Uri Endpoint { get; set; } = new("https://api.x.ai");

    /// <summary>Gets or sets the gRPC channel options.</summary>
    public GrpcChannelOptions? ChannelOptions { get; set; }

    /// <summary>Gets or sets the end user ID for the chat session.</summary>
    public string? EndUserId { get; set; }
}
