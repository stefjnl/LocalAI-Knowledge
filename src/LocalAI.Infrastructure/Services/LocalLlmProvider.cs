using System.Text.Json;
using LocalAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class LocalLlmProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly LocalAI.Core.Interfaces.IConfigurationProvider _configProvider;
        private readonly ILogger<LocalLlmProvider> _logger;

        public LocalLlmProvider(
            HttpClient httpClient,
            LocalAI.Core.Interfaces.IConfigurationProvider configProvider,
            ILogger<LocalLlmProvider> logger)
        {
            _httpClient = httpClient;
            _configProvider = configProvider;
            _logger = logger;
        }

        public string GetProviderName() => "LocalLLM";

        public bool CanHandle(string providerType)
        {
            return string.Equals(providerType, "local", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(providerType, "localai", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> GenerateResponseAsync(string prompt)
        {
            var baseUrl = _configProvider.GetRagBaseUrl();
            var model = _configProvider.GetRagModel();

            var payload = JsonSerializer.Serialize(new
            {
                model = model,
                prompt = prompt,
                stream = false,
                temperature = 0.1,
                max_tokens = 2000,
                top_p = 0.95,
                frequency_penalty = 0.0,
                presence_penalty = 0.0
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            try
            {
                var endpoint = $"{baseUrl}/v1/completions";
                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return result.GetProperty("choices")[0].GetProperty("text").GetString() ?? "No response generated.";
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
