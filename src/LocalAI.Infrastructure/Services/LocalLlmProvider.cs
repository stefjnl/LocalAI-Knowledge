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

            // Parse the combined prompt to separate system and user parts
            // The RAG service combines them with \n\n as separator
            string systemContent, userContent;
            var promptParts = prompt.Split(new[] { "\n\n" }, 2, StringSplitOptions.None);

            if (promptParts.Length == 2)
            {
                // First part is system prompt, second part is user prompt
                systemContent = promptParts[0];
                userContent = promptParts[1];
            }
            else
            {
                // Fallback to default system prompt if not properly formatted
                systemContent = "You are a helpful assistant that provides accurate information based on the provided context. Answer the user's question concisely and factually.";
                userContent = prompt;
            }

            // Use chat completions endpoint instead of completions for better results
            var payload = JsonSerializer.Serialize(new
            {
                model = model,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = systemContent
                    },
                    new
                    {
                        role = "user",
                        content = userContent
                    }
                },
                stream = false,
                temperature = 0.1,
                max_tokens = 2000,
                top_p = 0.95,
                frequency_penalty = 0.0,
                presence_penalty = 0.0,
                stop = new[] { "<|im_end|>", "<|endoftext|>" }
            });

            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            try
            {
                // Try chat completions endpoint first (more modern approach)
                var endpoint = $"{baseUrl}/v1/chat/completions";
                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("LLM API response: {ResponseContent}", responseContent);

                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    // Handle both chat completions and completions response formats
                    if (result.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];

                        // Try to get message content (chat completions format)
                        if (firstChoice.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("content", out var contentProp))
                        {
                            return contentProp.GetString()?.Trim() ?? "No response generated.";
                        }

                        // Try to get text (completions format)
                        if (firstChoice.TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString()?.Trim() ?? "No response generated.";
                        }
                    }

                    return "No valid response generated from the model.";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("LLM API error: Status {StatusCode}, Content: {ErrorContent}",
                        response.StatusCode, errorContent);

                    // Fallback to completions endpoint if chat completions fails
                    return await TryCompletionsEndpoint(baseUrl, prompt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to LLM");
                return $"❌ Error connecting to LLM: {ex.Message}";
            }
        }

        private async Task<string> TryCompletionsEndpoint(string baseUrl, string prompt)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    model = _configProvider.GetRagModel(),
                    prompt = prompt,
                    stream = false,
                    temperature = 0.1,
                    max_tokens = 2000,
                    top_p = 0.95,
                    frequency_penalty = 0.0,
                    presence_penalty = 0.0,
                    stop = new[] { "<|im_end|>", "<|endoftext|>" }
                });

                var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var endpoint = $"{baseUrl}/v1/completions";
                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (result.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("text", out var textProp))
                    {
                        return textProp.GetString()?.Trim() ?? "No response generated.";
                    }
                }

                return "❌ Failed to get response from both chat and completions endpoints.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error with completions endpoint fallback");
                return $"❌ Error with fallback endpoint: {ex.Message}";
            }
        }
    }
}
