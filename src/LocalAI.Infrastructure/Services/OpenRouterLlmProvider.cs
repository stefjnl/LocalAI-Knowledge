using System.Text.Json;
using LocalAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class OpenRouterLlmProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly LocalAI.Core.Interfaces.IConfigurationProvider _configProvider;
        private readonly ILogger<OpenRouterLlmProvider> _logger;

        public OpenRouterLlmProvider(
            HttpClient httpClient,
            LocalAI.Core.Interfaces.IConfigurationProvider configProvider,
            ILogger<OpenRouterLlmProvider> logger)
        {
            _httpClient = httpClient;
            _configProvider = configProvider;
            _logger = logger;
        }

        public string GetProviderName() => "OpenRouter";

        public bool CanHandle(string providerType)
        {
            return string.Equals(providerType, "openrouter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(providerType, "qwen3-coder", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> GenerateResponseAsync(string prompt)
        {
            var baseUrl = _configProvider.GetRagBaseUrl();
            var model = _configProvider.GetRagModel();

            var payload = JsonSerializer.Serialize(new
            {
                model = model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                stream = false,
                temperature = 0.1,
                max_tokens = 2000,
                top_p = 0.95,
                frequency_penalty = 0.0,
                presence_penalty = 0.0,
                provider = new
                {
                    order = new[] { "OpenRouter", "Azure", "LocalAI" }
                }
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            try
            {
                // Add OpenRouter specific headers
                _httpClient.DefaultRequestHeaders.Authorization = null;
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
                _httpClient.DefaultRequestHeaders.Remove("X-Title");

                // Get API key from environment
                var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ??
                            System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ??
                            "YOUR_API_KEY_HERE";

                _logger.LogInformation("[DEBUG] Using API Key: {ApiKey}",
                    apiKey.Length > 10 ? apiKey.Substring(0, 5) + "..." + apiKey.Substring(apiKey.Length - 5) : "INVALID");

                if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_API_KEY_HERE")
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:7001");
                    _httpClient.DefaultRequestHeaders.Add("X-Title", "LocalAI Knowledge Assistant");
                }
                else
                {
                    _logger.LogError("[ERROR] OpenRouter API key not found in environment variables or configuration");
                    return "❌ Error: OpenRouter API key not configured. Please check your .env file.";
                }

                // Determine endpoint
                var endpoint = $"{baseUrl}/chat/completions";
                _logger.LogInformation("[DEBUG] Sending request to endpoint: {Endpoint}", endpoint);

                var response = await _httpClient.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "No response generated.";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[ERROR] LLM API error: {ErrorContent}", errorContent);
                    return $"❌ LLM API error: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Error connecting to LLM");
                return $"❌ Error connecting to LLM: {ex.Message}";
            }
        }
    }
}
