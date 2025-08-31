using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services;

public class ConversationSummarizationService : IConversationSummarizationService
{
    private readonly ILogger<ConversationSummarizationService> _logger;
    private readonly IRAGService _ragService;

    public ConversationSummarizationService(
        ILogger<ConversationSummarizationService> logger,
        IRAGService ragService)
    {
        _logger = logger;
        _ragService = ragService;
    }

    public async Task<ConversationSummary> GenerateSummaryAsync(ChatConversation conversation)
    {
        return await GenerateSummaryAsync(conversation.Messages, conversation.Id);
    }

    public async Task<ConversationSummary> GenerateSummaryAsync(List<ConversationMessage> messages, Guid conversationId)
    {
        try
        {
            if (!messages.Any())
            {
                return new ConversationSummary
                {
                    Content = "No messages to summarize",
                    SummaryType = "auto",
                    OriginalMessageCount = 0,
                    KeyTopics = new List<string>(),
                    FollowUpSuggestions = new List<string>()
                };
            }

            // Extract key topics first
            var keyTopics = await ExtractKeyTopicsAsync(messages);

            // Generate summary content
            var summaryContent = await GenerateSummaryContentAsync(messages);

            // Generate follow-up suggestions
            var followUpSuggestions = await GenerateFollowUpSuggestionsFromMessagesAsync(messages);

            return new ConversationSummary
            {
                Content = summaryContent,
                SummaryType = "auto",
                OriginalMessageCount = messages.Count,
                KeyTopics = keyTopics,
                FollowUpSuggestions = followUpSuggestions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating conversation summary for conversation {ConversationId}", conversationId);

            // Return a basic fallback summary
            return new ConversationSummary
            {
                Content = $"Conversation with {messages.Count} messages",
                SummaryType = "auto",
                OriginalMessageCount = messages.Count,
                KeyTopics = new List<string>(),
                FollowUpSuggestions = new List<string>()
            };
        }
    }

    public async Task<bool> UpdateSummaryAsync(Guid conversationId, ConversationSummary summary)
    {
        try
        {
            // This would typically update the summary in the database
            // For now, we'll just log it
            _logger.LogInformation("Updated summary for conversation {ConversationId}: {SummaryContent}",
                conversationId, summary.Content);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating summary for conversation {ConversationId}", conversationId);
            return false;
        }
    }

    public async Task<ConversationSummary?> GetSummaryAsync(Guid conversationId)
    {
        try
        {
            // This would typically retrieve the summary from the database
            // For now, return null to indicate no cached summary
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving summary for conversation {ConversationId}", conversationId);
            return null;
        }
    }

    public async Task<List<string>> ExtractKeyTopicsAsync(List<ConversationMessage> messages)
    {
        try
        {
            if (!messages.Any())
                return new List<string>();

            // Simple keyword extraction (could be enhanced with NLP)
            var userMessages = messages.Where(m => m.Role == "user").ToList();
            if (!userMessages.Any())
                return new List<string>();

            var allContent = string.Join(" ", userMessages.Select(m => m.Content));
            var words = allContent.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .GroupBy(w => w.ToLower())
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList();

            return words;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting key topics");
            return new List<string>();
        }
    }

    public async Task<List<string>> GenerateFollowUpSuggestionsAsync(ChatConversation conversation, int maxSuggestions = 5)
    {
        return await GenerateFollowUpSuggestionsFromMessagesAsync(conversation.Messages, maxSuggestions);
    }

    public async Task<bool> ShouldGenerateSummaryAsync(ChatConversation conversation)
    {
        // Generate summary if conversation has more than 10 messages or is older than 1 hour
        var messageCountThreshold = 10;
        var ageThreshold = TimeSpan.FromHours(1);

        return conversation.Messages.Count >= messageCountThreshold ||
               (DateTime.UtcNow - conversation.CreatedAt) >= ageThreshold;
    }

    private async Task<string> GenerateSummaryContentAsync(List<ConversationMessage> messages)
    {
        try
        {
            // Create a prompt for the LLM to generate a summary
            var conversationText = BuildConversationText(messages);
            var prompt = $"Please provide a concise summary of the following conversation:\n\n{conversationText}\n\nSummary:";

            // Use the RAG service to generate the summary
            var emptySearchResults = new List<SearchResult>();
            var summaryResponse = await _ragService.GenerateResponseAsync(prompt, emptySearchResults);

            if (!string.IsNullOrEmpty(summaryResponse))
            {
                return summaryResponse.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary content with LLM");
        }

        // Fallback: Generate a simple summary
        var userMessageCount = messages.Count(m => m.Role == "user");
        var assistantMessageCount = messages.Count(m => m.Role == "assistant");

        return $"Conversation with {userMessageCount} user messages and {assistantMessageCount} assistant responses.";
    }

    private async Task<List<string>> GenerateFollowUpSuggestionsFromMessagesAsync(List<ConversationMessage> messages, int maxSuggestions = 5)
    {
        try
        {
            if (!messages.Any())
                return new List<string>();

            var lastUserMessage = messages.LastOrDefault(m => m.Role == "user");
            if (lastUserMessage == null)
                return new List<string>();

            // Generate follow-up suggestions based on the last user message
            var suggestions = new List<string>();

            // Simple pattern-based suggestions (could be enhanced with LLM)
            var content = lastUserMessage.Content.ToLower();

            if (content.Contains("how") || content.Contains("what"))
            {
                suggestions.Add("Can you provide more details about that?");
                suggestions.Add("What are the next steps?");
            }

            if (content.Contains("why"))
            {
                suggestions.Add("Can you explain the reasoning behind that?");
                suggestions.Add("What are the alternatives?");
            }

            if (content.Contains("code") || content.Contains("implement"))
            {
                suggestions.Add("Can you show me the implementation?");
                suggestions.Add("What are the best practices for this?");
            }

            // Add generic follow-ups
            suggestions.Add("Can you elaborate on that?");
            suggestions.Add("What are the implications?");
            suggestions.Add("How does this compare to other approaches?");

            return suggestions.Take(maxSuggestions).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating follow-up suggestions");
            return new List<string>();
        }
    }

    private string BuildConversationText(List<ConversationMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages.OrderBy(m => m.Timestamp))
        {
            var role = message.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"{role}: {message.Content}");
        }

        return sb.ToString();
    }
}
