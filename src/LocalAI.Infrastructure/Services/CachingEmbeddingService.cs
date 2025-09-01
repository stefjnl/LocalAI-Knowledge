using LocalAI.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class CachingEmbeddingService : IEmbeddingService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachingEmbeddingService> _logger;
        private readonly IConfiguration _configuration;

        public CachingEmbeddingService(
            IEmbeddingService embeddingService,
            IMemoryCache cache,
            ILogger<CachingEmbeddingService> logger,
            IConfiguration configuration)
        {
            _embeddingService = embeddingService;
            _cache = cache;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false)
        {
            var cacheKey = $"embedding_{(isQuery ? "query" : "doc")}_{text}";
            
            if (_cache.TryGetValue(cacheKey, out float[] cachedEmbedding))
            {
                _logger.LogDebug("Embedding retrieved from cache for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
                return cachedEmbedding;
            }

            var embedding = await _embeddingService.GenerateEmbeddingAsync(text, isQuery);
            
            // Get caching configuration
            var slidingExpirationMinutes = _configuration.GetValue<int>("Caching:Embeddings:SlidingExpirationMinutes", 30);
            var absoluteExpirationMinutes = _configuration.GetValue<int>("Caching:Embeddings:AbsoluteExpirationMinutes", 120);
            
            // Cache with sliding expiration
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(slidingExpirationMinutes))
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(absoluteExpirationMinutes))
                .SetSize(1); // Size-based eviction (each embedding counts as 1 unit)
            
            _cache.Set(cacheKey, embedding, cacheEntryOptions);
            _logger.LogDebug("Embedding cached for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
            
            return embedding;
        }
    }
}