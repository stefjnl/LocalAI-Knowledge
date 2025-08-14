using System.Text;
using System.Text.Json;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;

namespace LocalAI.Infrastructure.Services
{
    public class RAGService : IRAGService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _model;

        public RAGService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _baseUrl = configuration["RAGService:BaseUrl"] ?? "http://localhost:1234";
            _model = configuration["RAGService:Model"] ?? "qwen2.5-coder-7b-instruct";
        }

        public async Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults)
        {
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Based on the following knowledge sources, provide a comprehensive answer:");
            contextBuilder.AppendLine();

            for (int i = 0; i < searchResults.Count; i++)
            {
                var result = searchResults[i];
                contextBuilder.AppendLine($"Source {i + 1} ({result.Source}, Score: {result.Score:F2}):");
                contextBuilder.AppendLine($"\"{result.Content}\"");
                contextBuilder.AppendLine();
            }

            contextBuilder.AppendLine($"Question: {query}");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("Please provide a detailed, well-structured answer that synthesizes information from the sources above. Include relevant details and examples when available:");

            var prompt = contextBuilder.ToString();

            var payload = JsonSerializer.Serialize(new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful AI assistant that provides detailed, accurate answers based on the provided source material. Synthesize information from multiple sources when relevant." },
                    new { role = "user", content = prompt }
                },
                stream = false,
                temperature = 0.7,
                max_tokens = 1000
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "No response generated.";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return $"❌ LLM API error: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error connecting to LLM: {ex.Message}";
            }
        }
    }
}