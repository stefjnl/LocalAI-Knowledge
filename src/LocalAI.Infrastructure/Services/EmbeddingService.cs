using LocalAI.Core.Interfaces;

namespace LocalAI.Infrastructure.Services
{
    public class EmbeddingService : IEmbeddingService
    {
        private readonly IEmbeddingProvider _embeddingProvider;

        public EmbeddingService(IEmbeddingProvider embeddingProvider)
        {
            _embeddingProvider = embeddingProvider;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false)
        {
            return await _embeddingProvider.GenerateEmbeddingAsync(text, isQuery);
        }
    }
}
