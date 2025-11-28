using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace Devlooped.Grok
{
    public static class ModelsClientExtensions
    {
        extension(Models.ModelsClient client)
        {
            public async Task<IEnumerable<LanguageModel>> ListLanguageModelsAsync(CancellationToken cancellation = default)
            {
                var models = await client.ListLanguageModelsAsync(new Empty(), cancellationToken: cancellation);
                return models.Models;
            }
        }
    }
}

namespace Devlooped.Grok
{
    /// <summary>
    /// An API service that let users get details of available models on the
    /// platform.
    /// </summary>
    partial class Models
    {
        static Models() => __ServiceName = "xai_api.Models";
    }
}