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
            _model = "qwen2.5-coder-7b-instruct";
        }

        public async Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults)
        {
            // OPTIMIZATION 1: Truncate and summarize context instead of including everything
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Relevant knowledge:");

            for (int i = 0; i < Math.Min(searchResults.Count, 5); i++) // Limit to top 5 results
            {
                var result = searchResults[i];
                // OPTIMIZATION 2: Truncate each result to max 200 characters
                var truncatedContent = result.Content.Length > 200
                    ? result.Content.Substring(0, 200) + "..."
                    : result.Content;

                contextBuilder.AppendLine($"{i + 1}. {truncatedContent} (Source: {result.Source})");
            }

            contextBuilder.AppendLine($"\nQuestion: {query}");
            contextBuilder.AppendLine("Answer:");

            var prompt = contextBuilder.ToString();

            var payload = JsonSerializer.Serialize(new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant. Answer based on the provided knowledge. Be concise and accurate." },
                    new { role = "user", content = prompt }
                },
                stream = false,
                temperature = 0.0,        // OPTIMIZATION 3: Lower temperature for faster, more deterministic responses
                max_tokens = 500,         // OPTIMIZATION 4: Reduced max tokens for faster generation
                // OPTIMIZATION 5: Add draft model for speculative decoding (if supported)
                draft_model = "qwen2.5-coder-7b-instruct"  // Smaller model for faster inference
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