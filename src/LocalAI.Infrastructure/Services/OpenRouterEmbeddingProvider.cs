using System.Text.Json;
using LocalAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class OpenRouterEmbeddingProvider : IEmbeddingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly LocalAI.Core.Interfaces.IConfigurationProvider _configProvider;
        private readonly ILogger<OpenRouterEmbeddingProvider> _logger;

        public OpenRouterEmbeddingProvider(
            HttpClient httpClient,
            LocalAI.Core.Interfaces.IConfigurationProvider configProvider,
            ILogger<OpenRouterEmbeddingProvider> logger)
        {
            _httpClient = httpClient;
            _configProvider = configProvider;
            _logger = logger;
        }

        public string GetProviderName() => "OpenRouter";

        public async Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false)
        {
            var baseUrl = _configProvider.GetEmbeddingBaseUrl();
            var model = _configProvider.GetEmbeddingModel();

            var prefix = isQuery ? "search_query: " : "search_document: ";
            var prefixedText = $"{prefix}{text}";

            var payload = JsonSerializer.Serialize(new
            {
                input = prefixedText,
                model = model
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            // Determine if we're using OpenRouter
            var isOpenRouter = baseUrl.Contains("openrouter.ai");

            // Determine endpoint based on whether we're using OpenRouter
            var endpoint = isOpenRouter ? $"{baseUrl}/embeddings" : $"{baseUrl}/v1/embeddings";

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
