using System.ComponentModel;
using Google.Protobuf.WellKnownTypes;

namespace xAI.Protocol;

/// <summary>
/// Usability extensions for Grok gRPC clients.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrpcExtensions
{
    extension(Models.ModelsClient client)
    {
        /// <summary>Lists available language models.</summary>
        public async Task<IEnumerable<LanguageModel>> ListLanguageModelsAsync(CancellationToken cancellation = default)
        {
            var models = await client.ListLanguageModelsAsync(new Empty(), cancellationToken: cancellation);
            return models.Models;
        }

        /// <summary>Lists available embedding models.</summary>
        public async Task<IEnumerable<EmbeddingModel>> ListEmbeddingModelsAsync(CancellationToken cancellation)
        {
            var models = await client.ListEmbeddingModelsAsync(new Empty(), cancellationToken: cancellation);
            return models.Models;
        }

        /// <summary>Lists available image generation models.</summary>
        public async Task<IEnumerable<ImageGenerationModel>> ListImageGenerationModelsAsync(CancellationToken cancellation = default)
        {
            var models = await client.ListImageGenerationModelsAsync(new Empty(), cancellationToken: cancellation);
            return models.Models;
        }
    }
}
