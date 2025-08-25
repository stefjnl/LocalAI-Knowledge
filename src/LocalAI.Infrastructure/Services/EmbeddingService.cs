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
            
            // Determine if we're using OpenRouter
            var isOpenRouter = _baseUrl.Contains("openrouter.ai");
            
            // Determine endpoint based on whether we're using OpenRouter
            var endpoint = isOpenRouter ? $"{_baseUrl}/embeddings" : $"{_baseUrl}/v1/embeddings";
            
            try
            {
                // Add OpenRouter specific headers if needed
                if (isOpenRouter)
                {
                    // Remove any existing auth header first
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                    _httpClient.DefaultRequestHeaders.Remove("Authorization");
                    _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
                    _httpClient.DefaultRequestHeaders.Remove("X-Title");
                    
                    // Get API key from environment or config
                    var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? 
                                System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ??
                                "YOUR_API_KEY_HERE"; // This will help us debug if the key isn't set
                    
                    if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_API_KEY_HERE")
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:7001");
                        _httpClient.DefaultRequestHeaders.Add("X-Title", "LocalAI Knowledge Assistant");
                    }
                    else
                    {
                        throw new Exception("OpenRouter API key not configured. Please check your .env file.");
                    }
                }

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Embedding API failed: {response.StatusCode} - {responseContent}");
                }

                try
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var embeddingArray = result.GetProperty("data")[0].GetProperty("embedding").EnumerateArray();
                    return embeddingArray.Select(x => (float)x.GetDouble()).ToArray();
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to parse embedding response: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating embedding: {ex.Message}");
            }
        }
    }
}
