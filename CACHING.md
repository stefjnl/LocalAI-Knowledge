# Caching Implementation

This document describes the caching strategy implemented in the LocalAI Knowledge application to improve performance and reduce redundant operations.

## Overview

The caching implementation uses .NET's built-in MemoryCache to cache expensive operations such as:
1. Vector search results
2. Embedding generation
3. RAG (Retrieval-Augmented Generation) responses

## Implementation Details

### 1. Caching Services

Three caching decorator services have been implemented:

#### CachingVectorSearchService
- Caches search results from the vector database
- Caches collection existence checks
- Invalidates cache when documents are added or removed

#### CachingEmbeddingService
- Caches embedding generation for both documents and queries
- Reduces calls to the embedding model API

#### CachingRAGService
- Caches LLM responses based on query and search results
- Reduces calls to the LLM API for repeated queries

### 2. Cache Configuration

Caching behavior is configurable through `appsettings.json`:

```json
{
  "Caching": {
    "SearchResults": {
      "SlidingExpirationMinutes": 10,
      "AbsoluteExpirationMinutes": 60
    },
    "Embeddings": {
      "SlidingExpirationMinutes": 30,
      "AbsoluteExpirationMinutes": 120
    },
    "RAGResponses": {
      "SlidingExpirationMinutes": 15,
      "AbsoluteExpirationMinutes": 60
    }
  }
}
```

### 3. Cache Keys

Each service uses specific cache keys to ensure proper cache invalidation:

- **Vector Search**: `search_{query}_{limit}`
- **Collection Existence**: `collection_exists_{collectionName}`
- **Embeddings**: `embedding_{type}_{text}` (where type is "query" or "doc")
- **RAG Responses**: `rag_response_{query}_{searchResults}_{context}`

### 4. Cache Invalidation

Cache invalidation occurs when:
- New documents are processed and stored
- Documents are deleted
- The cache entry expires (sliding or absolute)

## Performance Benefits

1. **Reduced API Calls**: Fewer calls to LM Studio/OpenRouter for embeddings and RAG responses
2. **Faster Response Times**: Cached results return immediately
3. **Lower Resource Usage**: Reduced CPU/GPU load on embedding and LLM services
4. **Improved Scalability**: Better handling of repeated queries

## Dependencies

The implementation uses:
- `Microsoft.Extensions.Caching.Memory` for in-memory caching
- `Scrutor` for service decoration pattern

## Configuration

To adjust caching behavior, modify the values in `appsettings.json`. The sliding expiration determines how long an item remains in cache after last access, while absolute expiration ensures items don't remain in cache indefinitely.

  1. Three Caching Decorator Services
   - CachingVectorSearchService - Caches vector search results and collection checks
   - CachingEmbeddingService - Caches embedding generation operations
   - CachingRAGService - Caches LLM responses

  2. Proper Service Registration
   - Used Scrutor's Decorate method to wrap the original services
   - Maintained the decorator pattern for clean separation of concerns
   - Added necessary dependencies (MemoryCache, Scrutor) to project files

  3. Configurable Caching
   - Added caching configuration to appsettings.json
   - Implemented sliding and absolute expiration policies
   - Made cache durations configurable per service type

  4. Intelligent Cache Keys
   - Used context-aware cache keys to ensure proper caching
   - Included query parameters, limits, and context in cache keys
   - Proper cache invalidation when documents are added/removed

  5. Memory Management
   - Used size-based eviction to prevent memory exhaustion
   - Implemented proper cache entry options with expiration policies

  The implementation follows .NET best practices and should provide significant performance improvements by:
   - Reducing redundant API calls to LM Studio/OpenRouter
   - Caching expensive embedding generation operations
   - Avoiding repeated vector searches for the same queries
   - Caching LLM responses for identical query/result combinations

  The caching is completely transparent to the rest of the application thanks to the decorator pattern, and can be easily tuned through configuration settings without code changes.