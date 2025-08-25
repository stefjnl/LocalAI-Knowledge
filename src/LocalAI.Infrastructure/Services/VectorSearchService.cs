using System.Text.Json;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;

namespace LocalAI.Infrastructure.Services
{
    public class VectorSearchService : IVectorSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly IEmbeddingService _embeddingService;
        private readonly string _baseUrl;
        private readonly string _collectionName;

        public VectorSearchService(
            HttpClient httpClient,
            IEmbeddingService embeddingService,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _embeddingService = embeddingService;
            _baseUrl = configuration["Qdrant:BaseUrl"] ?? "http://localhost:6333";
            _collectionName = configuration["Qdrant:CollectionName"] ?? "knowledge";
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
                    throw new Exception($"Failed to create collection: HTTP {createResponse.StatusCode} - {errorContent}");
                }
                else
                {
                    Console.WriteLine($"✅ Collection '{_collectionName}' created successfully with indexing_threshold=500");
                }
            }

            // Store documents in batches
            const int batchSize = 100;
            var chunkList = chunks.ToList();

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

                var response = await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}/points", pointsContent);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ HTTP {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"❌ Request URL: {_baseUrl}/collections/{_collectionName}/points");
                    Console.WriteLine($"❌ Payload sample: {pointsPayload.Substring(0, Math.Min(200, pointsPayload.Length))}...");
                    throw new Exception($"Failed to store batch {i / batchSize + 1}: HTTP {response.StatusCode} - {errorContent}");
                }
            }

            // FORCE INDEX BUILD after storing all points
            try
            {
                Console.WriteLine("🔨 Attempting to force index build...");
                var indexResponse = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/index",
                    new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

                Console.WriteLine($"🔨 Index build response: HTTP {indexResponse.StatusCode}");

                if (indexResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("✅ Index build initiated successfully");
                }
                else
                {
                    var errorContent = await indexResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"⚠️ Index build failed: HTTP {indexResponse.StatusCode} - {errorContent}");
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
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Index build error: {ex.Message}");
            }
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int limit = 5)
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, isQuery: true);

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
            var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/query", searchContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Search error: {responseContent}");
            }

            var searchResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var results = searchResult.GetProperty("result").GetProperty("points").EnumerateArray().ToList();

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
}