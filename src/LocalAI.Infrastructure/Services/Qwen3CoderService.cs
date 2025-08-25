
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalAI.Infrastructure.Services
{
    public class Qwen3CoderService : ICodeAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public Qwen3CoderService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _configuration = configuration;
        }

        public async Task<string> GenerateCodeResponseAsync(string query, List<ConversationExchange> conversationContext)
        {
            var apiKey = _configuration["OpenRouter:ApiKey"];
            var model = _configuration["OpenRouter:Model"];
            var endpoint = _configuration["OpenRouter:Endpoint"];

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(endpoint))
            {
                return "OpenRouter is not configured. Please check your appsettings.json.";
            }

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var messages = new List<object>();
            messages.Add(new { role = "system", content = "You are a coding assistant. Provide clean, efficient, and well-documented code." });

            foreach (var exchange in conversationContext)
            {
                if (!string.IsNullOrEmpty(exchange.Query))
                {
                    messages.Add(new { role = "user", content = exchange.Query });
                }
                if (!string.IsNullOrEmpty(exchange.Response))
                {
                    messages.Add(new { role = "assistant", content = exchange.Response });
                }
            }

            messages.Add(new { role = "user", content = query });

            var requestBody = new
            {
                model = model,
                messages = messages
            };

            var jsonRequest = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(jsonResponse);
                return openAIResponse?.Choices?[0]?.Message?.Content ?? "No response from model.";
            }
            else
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                return $"Error: {response.StatusCode}\n{errorResponse}";
            }
        }

        private class OpenAIResponse
        {
            public List<Choice> Choices { get; set; }
        }

        private class Choice
        {
            public Message Message { get; set; }
        }

        private class Message
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }
    }
}
