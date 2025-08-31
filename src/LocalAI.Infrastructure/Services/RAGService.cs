using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services
{
    public class RAGService : IRAGService
    {
        private readonly IEnumerable<ILlmProvider> _llmProviders;
        private readonly IConfigurationProvider _configProvider;
        private readonly ILogger<RAGService> _logger;

        public RAGService(
            IEnumerable<ILlmProvider> llmProviders,
            IConfigurationProvider configProvider,
            ILogger<RAGService> logger)
        {
            _llmProviders = llmProviders;
            _configProvider = configProvider;
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

            // Build context from search results - limit to top 5 for token management
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("=== RELEVANT KNOWLEDGE ===\n");
            for (int i = 0; i < Math.Min(searchResults.Count, 5); i++)
            {
                var result = searchResults[i];
                contextBuilder.AppendLine($"Source {i + 1}: {result.Source}");
                contextBuilder.AppendLine($"Content: {result.Content}");
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

            var systemPrompt = @"You are an expert knowledge assistant that synthesizes information from technical documents, conference transcripts, and developer resources.

RESPONSE APPROACH:
- Lead with a direct answer to the user's specific question
- Synthesize information across multiple sources when available
- Distinguish between established facts and emerging trends/opinions
- Acknowledge when sources provide incomplete coverage of a topic

SOURCE HANDLING:
- Base responses strictly on the provided knowledge sources
- When sources conflict, present the different perspectives clearly
- If information is insufficient, state what's available and what's missing
- Prioritize more recent sources for rapidly evolving technical topics

TECHNICAL COMMUNICATION:
- Explain concepts clearly without unnecessary jargon
- Include practical examples and implementation details when available
- Connect abstract concepts to real-world applications
- Provide actionable next steps when relevant

RESPONSE STRUCTURE:
- Use natural paragraph organization for complex topics
- Group related information logically
- Include specific details and examples from sources
- End with relevant considerations or related topics when helpful

Quality over formatting: Focus on accurate, useful information rather than stylistic elements.";

            var userPrompt = $@"Based on the following knowledge sources, provide a comprehensive answer to the user's question:{conversationContextText}

KNOWLEDGE SOURCES:
{contextBuilder}

USER QUESTION: {query}

Provide a thorough, well-structured response that addresses the question completely while staying true to the source material.";

            // Log the final prompt being sent to the LLM
            _logger.LogInformation("[DEBUG] Final LLM Prompt: {Prompt}", userPrompt);

            // Determine which provider to use based on configuration
            var providerType = "local"; // Default to local
            // In a real implementation, this would come from configuration or user selection
            // For now, we'll use a simple approach to select the provider

            var provider = _llmProviders.FirstOrDefault(p => p.CanHandle(providerType)) ??
                          _llmProviders.FirstOrDefault();

            if (provider == null)
            {
                _logger.LogError("[ERROR] No LLM provider available");
                return "❌ Error: No LLM provider configured.";
            }

            // Combine system and user prompts for providers that expect a single prompt
            var fullPrompt = $"{systemPrompt}\n\n{userPrompt}";
            return await provider.GenerateResponseAsync(fullPrompt);
        }
    }
}
