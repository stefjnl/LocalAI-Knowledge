using System.Text;
using System.Text.Json;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class RAGService : IRAGService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly ILogger<RAGService> _logger;

        public RAGService(HttpClient httpClient, IConfiguration configuration, ILogger<RAGService> logger)
        {
            _httpClient = httpClient;
            _baseUrl = configuration["RAGService:BaseUrl"] ?? "http://localhost:1234";
            _model = configuration["RAGService:Model"] ?? LocalAI.Core.Models.Constants.DefaultChatModel;
            _logger = logger;
        }

        public async Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults)
        {
            return await GenerateResponseAsync(query, searchResults, new List<ConversationExchange>());
        }

        public async Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults, List<ConversationExchange> conversationContext)
        {
            // Log incoming parameters
            _logger.LogInformation("[DEBUG] RAG Service context parameter: {Count} exchanges received", conversationContext?.Count ?? 0);
            if (conversationContext != null && conversationContext.Any())
            {
                for (int i = 0; i < conversationContext.Count; i++)
                {
                    var exchange = conversationContext[i];
                    _logger.LogInformation("[DEBUG] RAG Context Exchange {Index} - User: {Query}", i + 1, exchange.Query);
                    _logger.LogInformation("[DEBUG] RAG Context Exchange {Index} - Assistant: {Response}", i + 1, exchange.Response);
                }
            }

            // OPTIMIZATION 1: Truncate and summarize context instead of including everything
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("=== RELEVANT KNOWLEDGE ===\n");
            for (int i = 0; i < Math.Min(searchResults.Count, 5); i++)
            {
                var result = searchResults[i];
                contextBuilder.AppendLine($"Source {i + 1}: {result.Source}");
                contextBuilder.AppendLine($"Content: {result.Content}");  // Full content, no truncation
                contextBuilder.AppendLine($"Relevance: {result.Score:F2}\n");
            }

            // Add conversation context to the prompt if available
            var conversationContextText = "";
            if (conversationContext != null && conversationContext.Any())
            {
                var contextText = string.Join("\n\n", conversationContext.Select(ex =>
                    $"User: {ex.Query}\nAssistant: {ex.Response}"));
                conversationContextText = $"\n\n=== CONVERSATION CONTEXT ===\n{contextText}\n\n";
            }

            var systemPrompt = @"You are an expert knowledge assistant. Respond like Claude Sonnet 4 with these characteristics:

                RESPONSE STRUCTURE:
                - Start with a clear, direct answer to the question
                - Organize information into logical sections when appropriate
                - Use practical examples and real-world applications
                - Provide actionable insights where relevant
                - Be comprehensive but concise

                STYLE GUIDELINES:
                - Use clear, professional language without jargon
                - Structure complex information with natural flow
                - Include specific details from the provided sources
                - When discussing technical concepts, explain practical implications
                - End with related considerations or next steps if helpful

                FORMATTING:
                - Use natural paragraph breaks for readability
                - Bold key concepts or important points
                - Present information in a logical, easy-to-follow sequence

                Answer based strictly on the provided knowledge sources. If the sources don't contain enough information for a complete answer, acknowledge this and work with what's available.";

            var userPrompt = $@"Based on the following knowledge sources, provide a comprehensive answer to the user's question:{conversationContextText}

                KNOWLEDGE SOURCES:
                {contextBuilder}

                USER QUESTION: {query}

                Provide a thorough, well-structured response that addresses the question completely while staying true to the source material.";

            // Log the final prompt being sent to the LLM
            _logger.LogInformation("[DEBUG] Final LLM Prompt: {Prompt}", userPrompt);

            var payload = JsonSerializer.Serialize(new
            {
                model = _model,
                messages = new[]
                {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                stream = false,
                temperature = 0.3,        // Slightly higher for more natural responses
                max_tokens = 1500,        // Increased for comprehensive responses
                top_p = 0.9,
                frequency_penalty = 0.1,
                presence_penalty = 0.1
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
