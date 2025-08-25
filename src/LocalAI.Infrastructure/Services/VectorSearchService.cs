using System.Text.Json;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class VectorSearchService : IVectorSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly IEmbeddingService _embeddingService;
        private readonly string _baseUrl;
        private readonly string _collectionName;
        private readonly ILogger<VectorSearchService>? _logger;

        public VectorSearchService(
            HttpClient httpClient,
            IEmbeddingService embeddingService,
            IConfiguration configuration,
            ILogger<VectorSearchService>? logger = null)
        {
            _httpClient = httpClient;
            _embeddingService = embeddingService;
            _baseUrl = configuration["Qdrant:BaseUrl"] ?? "http://localhost:6333";
            _collectionName = configuration["Qdrant:CollectionName"] ?? "knowledge";
            _logger = logger;
        }

        public async Task<bool> CollectionExistsAsync(string collectionName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/collections/{collectionName}");
                Console.WriteLine($"🔍 Collection existence check: HTTP {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var pointsCount = result.GetProperty("result").GetProperty("points_count").GetInt32();
                    var indexedCount = result.GetProperty("result").GetProperty("indexed_vectors_count").GetInt32();

                    Console.WriteLine($"📊 Collection found: {pointsCount} points, {indexedCount} indexed");
                    return pointsCount > 0;
                }

                Console.WriteLine("❌ Collection does not exist");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Collection check error: {ex.Message}");
                return false;
            }
        }

        public async Task StoreDocumentsAsync(IEnumerable<DocumentChunk> chunks)
        {
            var chunkList = chunks.ToList();
            _logger?.LogInformation("Starting to store {ChunkCount} document chunks", chunkList.Count);
            
            // FIXED: Create collection with correct optimization settings
            if (!await CollectionExistsAsync(_collectionName))
            {
                var collectionPayload = JsonSerializer.Serialize(new
                {
                    vectors = new { size = 768, distance = "Cosine" },
                    optimizers_config = new
                    {
                        indexing_threshold = 1,
                        vacuum_min_vector_number = 1,
                        default_segment_number = 8  // CRITICAL: Match your CPU cores (RTX 5070 Ti system likely has 8+ cores)
                    },
                    hnsw_config = new
                    {
                        m = 32,                     // Higher m for better accuracy
                        ef_construct = 200,         // Higher ef_construct for better index quality
                        full_scan_threshold = 10000 // Restore default threshold
                    }
                });

                var createContent = new StringContent(collectionPayload, System.Text.Encoding.UTF8, "application/json");
                var createResponse = await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}", createContent);

                if (!createResponse.IsSuccessStatusCode)
                {
                    var errorContent = await createResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Collection creation failed - HTTP {createResponse.StatusCode}: {errorContent}");
                    Console.WriteLine($"❌ Collection payload: {collectionPayload}");
                    _logger?.LogError("Collection creation failed - HTTP {StatusCode}: {ErrorContent}. Payload: {Payload}", 
                        createResponse.StatusCode, errorContent, collectionPayload);
                    throw new Exception($"Failed to create collection: HTTP {createResponse.StatusCode} - {errorContent}");
                }
                else
                {
                    Console.WriteLine($"✅ Collection '{_collectionName}' created successfully with indexing_threshold=500");
                    _logger?.LogInformation("Collection '{CollectionName}' created successfully", _collectionName);
                }
            }

            // Store documents in batches
            const int batchSize = 100;
            _logger?.LogInformation("Storing documents in batches of {BatchSize}", batchSize);

            for (int i = 0; i < chunkList.Count; i += batchSize)
            {
                var batch = chunkList.Skip(i).Take(batchSize);
                var points = batch.Select((item, index) => new
                {
                    id = i + index,
                    vector = item.Embedding,
                    payload = new
                    {
                        text = item.Text,
                        source = item.Source,
                        type = item.Metadata.Split('|')[0],
                        page_info = item.Metadata.Contains('|') ? item.Metadata.Split('|')[1] : ""
                    }
                }).ToArray();

                var pointsPayload = JsonSerializer.Serialize(new { points });
                var pointsContent = new StringContent(pointsPayload, System.Text.Encoding.UTF8, "application/json");
                
                _logger?.LogDebug("Storing batch {BatchNumber}/{TotalBatches} with {PointCount} points", 
                    (i / batchSize) + 1, Math.Ceiling((double)chunkList.Count / batchSize), points.Length);

                var response = await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}/points", pointsContent);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ HTTP {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"❌ Request URL: {_baseUrl}/collections/{_collectionName}/points");
                    Console.WriteLine($"❌ Payload sample: {pointsPayload.Substring(0, Math.Min(200, pointsPayload.Length))}...");
                    
                    _logger?.LogError("Failed to store batch {BatchNumber} - HTTP {StatusCode}: {ErrorContent}. URL: {Url}. Payload sample: {PayloadSample}",
                        (i / batchSize) + 1, response.StatusCode, errorContent, 
                        $"{_baseUrl}/collections/{_collectionName}/points",
                        pointsPayload.Substring(0, Math.Min(200, pointsPayload.Length)));
                    
                    throw new Exception($"Failed to store batch {i / batchSize + 1}: HTTP {response.StatusCode} - {errorContent}");
                }
                
                _logger?.LogDebug("Successfully stored batch {BatchNumber}/{TotalBatches}", 
                    (i / batchSize) + 1, Math.Ceiling((double)chunkList.Count / batchSize));
            }

            // FORCE INDEX BUILD after storing all points
            try
            {
                Console.WriteLine("🔨 Attempting to force index build...");
                _logger?.LogInformation("Attempting to force index build...");
                
                var indexResponse = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/index",
                    new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

                Console.WriteLine($"🔨 Index build response: HTTP {indexResponse.StatusCode}");
                _logger?.LogInformation("Index build response: HTTP {StatusCode}", indexResponse.StatusCode);

                if (indexResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("✅ Index build initiated successfully");
                    _logger?.LogInformation("Index build initiated successfully");
                }
                else
                {
                    var errorContent = await indexResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"⚠️ Index build failed: HTTP {indexResponse.StatusCode} - {errorContent}");
                    _logger?.LogWarning("Index build failed: HTTP {StatusCode} - {ErrorContent}", indexResponse.StatusCode, errorContent);
                }

                // Wait for indexing and verify status
                await Task.Delay(2000);

                var statusResponse = await _httpClient.GetAsync($"{_baseUrl}/collections/{_collectionName}");
                if (statusResponse.IsSuccessStatusCode)
                {
                    var statusContent = await statusResponse.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<JsonElement>(statusContent);
                    var indexedCount = status.GetProperty("result").GetProperty("indexed_vectors_count").GetInt32();
                    var totalCount = status.GetProperty("result").GetProperty("points_count").GetInt32();
                    Console.WriteLine($"📊 Final status: {indexedCount}/{totalCount} vectors indexed");
                    _logger?.LogInformation("Final status: {IndexedCount}/{TotalCount} vectors indexed", indexedCount, totalCount);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Index build error: {ex.Message}");
                _logger?.LogError(ex, "Index build error");
            }
            
            _logger?.LogInformation("Completed storing {ChunkCount} document chunks", chunkList.Count);
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int limit = 5)
        {
            _logger?.LogInformation("Starting search for query: {Query} with limit: {Limit}", query, limit);
            
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, isQuery: true);
            _logger?.LogDebug("Generated embedding for query");

            // Use optimized search parameters for speed
            var searchPayload = JsonSerializer.Serialize(new
            {
                query = queryEmbedding,
                limit = limit,
                with_payload = true,
                @params = new
                {
                    hnsw_ef = 128,          // HIGHER ef for better performance (not too low!)
                    exact = false,          // Force HNSW usage
                    indexed_only = true     // Only search indexed vectors
                }
            });

            var searchContent = new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json");
            _logger?.LogDebug("Sending search request to Qdrant");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/query", searchContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("Search failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new Exception($"Search error: {responseContent}");
            }

            var searchResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var results = searchResult.GetProperty("result").GetProperty("points").EnumerateArray().ToList();
            _logger?.LogInformation("Search completed with {ResultCount} results", results.Count);

            var searchResults = new List<SearchResult>();
            foreach (var result in results)
            {
                var score = result.GetProperty("score").GetSingle();
                var payload = result.GetProperty("payload");
                var text = payload.GetProperty("text").GetString() ?? "";
                var source = payload.GetProperty("source").GetString() ?? "";
                var type = payload.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "transcript";
                var pageInfo = payload.TryGetProperty("page_info", out var pageElement) ? pageElement.GetString() ?? "" : "";

                searchResults.Add(new SearchResult
                {
                    Content = text,
                    Source = GetSourceDisplayName(source, type, pageInfo),
                    Score = score,
                    Type = type,
                    PageInfo = pageInfo
                });
            }

            return searchResults;
        }

        // New debugging methods
        public async Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false)
        {
            return await _embeddingService.GenerateEmbeddingAsync(text, isQuery);
        }

        public async Task<CollectionInfo?> GetCollectionInfoAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/collections/{_collectionName}");
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<QdrantResponse<CollectionInfo>>(responseContent);
                    return result?.Result;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting collection info");
                return null;
            }
        }

        public async Task<List<DocumentChunkInfo>> GetDocumentChunksAsync(string sourceFilename)
        {
            try
            {
                var filterPayload = JsonSerializer.Serialize(new
                {
                    filter = new
                    {
                        must = new[]
                        {
                            new
                            {
                                key = "source",
                                match = new
                                {
                                    value = sourceFilename
                                }
                            }
                        }
                    },
                    limit = 1000,
                    with_payload = true,
                    with_vector = false
                });

                var filterContent = new StringContent(filterPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/scroll", filterContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogError("Failed to get document chunks: {StatusCode} - {Content}", response.StatusCode, responseContent);
                    return new List<DocumentChunkInfo>();
                }

                var scrollResult = JsonSerializer.Deserialize<QdrantResponse<ScrollResult>>(responseContent);
                var points = scrollResult?.Result?.Points ?? new List<QdrantPoint>();

                var chunks = new List<DocumentChunkInfo>();
                foreach (var point in points)
                {
                    var text = point.Payload?.TryGetProperty("text", out var textElement) == true ? textElement.GetString() ?? "" : "";
                    var source = point.Payload?.TryGetProperty("source", out var sourceElement) == true ? sourceElement.GetString() ?? "" : "";
                    var type = point.Payload?.TryGetProperty("type", out var typeElement) == true ? typeElement.GetString() ?? "" : "transcript";
                    var pageInfo = point.Payload?.TryGetProperty("page_info", out var pageElement) == true ? pageElement.GetString() ?? "" : "";

                    chunks.Add(new DocumentChunkInfo
                    {
                        Id = point.Id,
                        Content = text,
                        Source = source,
                        Type = type,
                        PageInfo = pageInfo
                    });
                }

                return chunks;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting document chunks for {Filename}", sourceFilename);
                return new List<DocumentChunkInfo>();
            }
        }

        public async Task<List<SearchResult>> RawSearchAsync(string query, int limit = 20)
        {
            try
            {
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, isQuery: true);

                var searchPayload = JsonSerializer.Serialize(new
                {
                    query = queryEmbedding,
                    limit = limit,
                    with_payload = true,
                    with_vector = false,
                    @params = new
                    {
                        hnsw_ef = 128,
                        exact = false,
                        indexed_only = true
                    }
                });

                var searchContent = new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/query", searchContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogError("Raw search failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
                    throw new Exception($"Search error: {responseContent}");
                }

                var searchResult = JsonSerializer.Deserialize<QdrantResponse<QueryResult>>(responseContent);
                var results = searchResult?.Result?.Points ?? new List<QdrantPoint>();

                var searchResults = new List<SearchResult>();
                foreach (var result in results)
                {
                    var score = result.Score ?? 0;
                    var text = result.Payload?.TryGetProperty("text", out var textElement) == true ? textElement.GetString() ?? "" : "";
                    var source = result.Payload?.TryGetProperty("source", out var sourceElement) == true ? sourceElement.GetString() ?? "" : "";
                    var type = result.Payload?.TryGetProperty("type", out var typeElement) == true ? typeElement.GetString() ?? "" : "transcript";
                    var pageInfo = result.Payload?.TryGetProperty("page_info", out var pageElement) == true ? pageElement.GetString() ?? "" : "";

                    searchResults.Add(new SearchResult
                    {
                        Content = text,
                        Source = GetSourceDisplayName(source, type, pageInfo),
                        Score = score,
                        Type = type,
                        PageInfo = pageInfo
                    });
                }

                return searchResults;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in raw search");
                throw;
            }
        }

        public async Task<bool> DeleteDocumentAsync(string documentName)
        {
            try
            {
                // Create filter to delete points with matching source
                var filterPayload = JsonSerializer.Serialize(new
                {
                    filter = new
                    {
                        must = new[]
                        {
                            new
                            {
                                key = "source",
                                match = new
                                {
                                    value = documentName
                                }
                            }
                        }
                    }
                });

                var filterContent = new StringContent(filterPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/delete", filterContent);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Successfully deleted document '{documentName}' from vector database");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Failed to delete document '{documentName}' - HTTP {response.StatusCode}: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting document '{documentName}': {ex.Message}");
                return false;
            }
        }

        private static string GetSourceDisplayName(string source, string type, string pageInfo)
        {
            if (type == "pdf")
            {
                var displayName = source.Replace("-", " ").Replace("_", " ");
                return string.IsNullOrEmpty(pageInfo) ? $"{displayName}.pdf" : $"{displayName}.pdf ({pageInfo})";
            }
            else if (type == "transcript")
            {
                return $"{source.Replace("-", " ")} Transcript";
            }

            return source;
        }
    }

    // Additional models for debugging
    public class CollectionInfo
    {
        public int PointsCount { get; set; }
        public int IndexedVectorsCount { get; set; }
        public VectorConfig? Vectors { get; set; }
    }

    public class VectorConfig
    {
        public int Size { get; set; }
        public string? Distance { get; set; }
    }

    public class QdrantResponse<T>
    {
        public T? Result { get; set; }
        public string? Status { get; set; }
        public int? Time { get; set; }
    }

    public class ScrollResult
    {
        public List<QdrantPoint> Points { get; set; } = new();
        public object? NextPageOffset { get; set; }
    }

    public class QueryResult
    {
        public List<QdrantPoint> Points { get; set; } = new();
    }

    public class QdrantPoint
    {
        public int Id { get; set; }
        public float? Score { get; set; }
        public JsonElement? Payload { get; set; }
        public float[]? Vector { get; set; }
    }
}