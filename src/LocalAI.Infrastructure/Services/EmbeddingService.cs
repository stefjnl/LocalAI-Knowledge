using System.Text.Json;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;

namespace LocalAI.Infrastructure.Services
{
    public class EmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _model;

        public EmbeddingService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _baseUrl = configuration["EmbeddingService:BaseUrl"] ?? "http://localhost:1234";
            _model = configuration["EmbeddingService:Model"] ?? LocalAI.Core.Models.Constants.DefaultChatModel;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false)
        {
            var prefix = isQuery ? "search_query: " : "search_document: ";
            var prefixedText = $"{prefix}{text}";

            var payload = JsonSerializer.Serialize(new
            {
                input = prefixedText,
                model = _model
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/v1/embeddings", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"LM Studio API failed: {response.StatusCode} - {responseContent}");
            }

            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var embeddingArray = result.GetProperty("data")[0].GetProperty("embedding").EnumerateArray();
                return embeddingArray.Select(x => (float)x.GetDouble()).ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse LM Studio response: {ex.Message}");
            }
        }
    }
}
