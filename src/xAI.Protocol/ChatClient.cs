using Grpc.Core;
using Grpc.Net.Client;

namespace xAI.Protocol;

partial class Chat
{
    partial class ChatClient
    {
        readonly object? options;

        internal ChatClient(ChannelBase channel, object options) : this(channel)
            => this.options = options;

        internal object? Options => options;
    }
}
