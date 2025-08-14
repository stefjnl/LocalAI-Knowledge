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

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var pointsCount = result.GetProperty("result").GetProperty("points_count").GetInt32();
                    return pointsCount > 0;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task StoreDocumentsAsync(IEnumerable<DocumentChunk> chunks)
        {
            // Create collection first
            var collectionPayload = JsonSerializer.Serialize(new
            {
                vectors = new { size = 768, distance = "Cosine" }
            });

            var createContent = new StringContent(collectionPayload, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PutAsync($"{_baseUrl}/collections/{_collectionName}", createContent);

            // Store points in batches
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
                    throw new Exception($"Failed to store batch {i / batchSize + 1}");
                }
            }
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int limit = 5)
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, isQuery: true);

            var searchPayload = JsonSerializer.Serialize(new
            {
                vector = queryEmbedding,
                limit = limit,
                with_payload = true
            });

            var searchContent = new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{_collectionName}/points/search", searchContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Search error: {responseContent}");
            }

            var searchResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
            var results = searchResult.GetProperty("result").EnumerateArray().ToList();

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