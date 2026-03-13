using Grpc.Core;
using Grpc.Net.Client;

namespace xAI.Protocol;

partial class Image
{
    partial class ImageClient
    {
        readonly object? options;

        internal ImageClient(ChannelBase channel, object options) : this(channel)
            => this.options = options;

        internal object? Options => options;
    }
}
