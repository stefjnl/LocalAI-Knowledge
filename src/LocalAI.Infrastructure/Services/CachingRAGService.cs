using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class CachingRAGService : IRAGService
    {
        private readonly IRAGService _ragService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachingRAGService> _logger;
        private readonly IConfiguration _configuration;

        public CachingRAGService(
            IRAGService ragService,
            IMemoryCache cache,
            ILogger<CachingRAGService> logger,
            IConfiguration configuration)
        {
            _ragService = ragService;
            _cache = cache;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults)
        {
            return await GenerateResponseAsync(query, searchResults, new List<ConversationExchange>());
        }

        public async Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults, List<ConversationExchange> conversationContext)
        {
            // Create a cache key based on query and search results
            var searchResultsKey = string.Join("|", searchResults.Select(r => $"{r.Source}:{r.Score:F2}").Take(5));
            var contextKey = string.Join("|", conversationContext.Select(c => $"{c.Query}:{c.Response}"));
            var cacheKey = $"rag_response_{query}_{searchResultsKey}_{contextKey}";

            if (_cache.TryGetValue(cacheKey, out string? cachedResponse) && !string.IsNullOrEmpty(cachedResponse))
            {
                _logger.LogDebug("RAG response retrieved from cache for query: {Query}", query);
                return cachedResponse;
            }

            var response = await _ragService.GenerateResponseAsync(query, searchResults, conversationContext);
            
            // Get caching configuration
            var slidingExpirationMinutes = _configuration.GetValue<int>("Caching:RAGResponses:SlidingExpirationMinutes", 15);
            var absoluteExpirationMinutes = _configuration.GetValue<int>("Caching:RAGResponses:AbsoluteExpirationMinutes", 60);
            
            // Cache with sliding expiration
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(slidingExpirationMinutes))
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(absoluteExpirationMinutes))
                .SetSize(response.Length / 100); // Rough size estimation (1 unit per 100 characters)
            
            _cache.Set(cacheKey, response, cacheEntryOptions);
            _logger.LogDebug("RAG response cached for query: {Query}", query);
            
            return response;
        }
    }
}