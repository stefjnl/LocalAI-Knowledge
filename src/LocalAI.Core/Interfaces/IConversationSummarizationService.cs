using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IConversationSummarizationService
{
    Task<ConversationSummary> GenerateSummaryAsync(ChatConversation conversation);
    Task<ConversationSummary> GenerateSummaryAsync(List<ConversationMessage> messages, Guid conversationId);
    Task<bool> UpdateSummaryAsync(Guid conversationId, ConversationSummary summary);
    Task<ConversationSummary?> GetSummaryAsync(Guid conversationId);
    Task<List<string>> ExtractKeyTopicsAsync(List<ConversationMessage> messages);
    Task<List<string>> GenerateFollowUpSuggestionsAsync(ChatConversation conversation, int maxSuggestions = 5);
    Task<bool> ShouldGenerateSummaryAsync(ChatConversation conversation);
}
