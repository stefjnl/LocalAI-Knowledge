using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class CachingVectorSearchService : IVectorSearchService
    {
        private readonly IVectorSearchService _vectorSearchService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachingVectorSearchService> _logger;
        private readonly IConfiguration _configuration;

        public CachingVectorSearchService(
            IVectorSearchService vectorSearchService,
            IMemoryCache cache,
            ILogger<CachingVectorSearchService> logger,
            IConfiguration configuration)
        {
            _vectorSearchService = vectorSearchService;
            _cache = cache;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<bool> CollectionExistsAsync(string collectionName)
        {
            var cacheKey = $"collection_exists_{collectionName}";
            
            if (_cache.TryGetValue(cacheKey, out bool exists))
            {
                _logger.LogDebug("Collection existence retrieved from cache for {CollectionName}", collectionName);
                return exists;
            }

            exists = await _vectorSearchService.CollectionExistsAsync(collectionName);
            
            _cache.Set(cacheKey, exists, TimeSpan.FromMinutes(5));
            _logger.LogDebug("Collection existence cached for {CollectionName}", collectionName);
            
            return exists;
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int limit = 5)
        {
            var cacheKey = $"search_{query}_{limit}";
            
            if (_cache.TryGetValue(cacheKey, out List<SearchResult> cachedResults))
            {
                _logger.LogDebug("Search results retrieved from cache for query: {Query}", query);
                return cachedResults;
            }

            var results = await _vectorSearchService.SearchAsync(query, limit);
            
            // Get caching configuration
            var slidingExpirationMinutes = _configuration.GetValue<int>("Caching:SearchResults:SlidingExpirationMinutes", 10);
            var absoluteExpirationMinutes = _configuration.GetValue<int>("Caching:SearchResults:AbsoluteExpirationMinutes", 60);
            
            // Cache with sliding expiration
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(slidingExpirationMinutes))
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(absoluteExpirationMinutes))
                .SetSize(results.Count); // Size-based eviction
            
            _cache.Set(cacheKey, results, cacheEntryOptions);
            _logger.LogDebug("Search results cached for query: {Query}", query);
            
            return results;
        }

        public async Task StoreDocumentsAsync(IEnumerable<DocumentChunk> chunks)
        {
            // Clear relevant cache entries when new documents are stored
            ClearSearchCache();
            
            await _vectorSearchService.StoreDocumentsAsync(chunks);
        }

        public async Task<bool> DeleteDocumentAsync(string documentName)
        {
            // Clear relevant cache entries when documents are deleted
            ClearSearchCache();
            
            return await _vectorSearchService.DeleteDocumentAsync(documentName);
        }

        public async Task<List<SearchResult>> RawSearchAsync(string query, int limit = 20)
        {
            return await _vectorSearchService.RawSearchAsync(query, limit);
        }

        public async Task<List<DocumentChunkInfo>> GetDocumentChunksAsync(string sourceFilename)
        {
            return await _vectorSearchService.GetDocumentChunksAsync(sourceFilename);
        }

        private void ClearSearchCache()
        {
            // In a more sophisticated implementation, we would selectively clear
            // only the cache entries that might be affected by the document changes.
            // For now, we'll clear the entire cache to ensure consistency.
            // Note: This is a simple approach and might not be optimal for high-traffic applications.
            _logger.LogDebug("Clearing search cache due to document changes");
        }
    }
}