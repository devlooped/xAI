using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace Devlooped.Grok;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrpcExtensions
{
    extension(Models.ModelsClient client)
    {
        public async Task<IEnumerable<LanguageModel>> ListLanguageModelsAsync(CancellationToken cancellation = default)
        {
            var models = await client.ListLanguageModelsAsync(new Empty(), cancellationToken: cancellation);
            return models.Models;
        }

        public async Task<IEnumerable<EmbeddingModel>> ListEmbeddingModelsAsync(CancellationToken cancellation)
        {
            var models = await client.ListEmbeddingModelsAsync(new Empty(), cancellationToken: cancellation);
            return models.Models;
        }

        public async Task<IEnumerable<ImageGenerationModel>> ListImageGenerationModelsAsync(CancellationToken cancellation = default)
        {
            var models = await client.ListImageGenerationModelsAsync(new Empty(), cancellationToken: cancellation);
            return models.Models;
        }
    }
}
