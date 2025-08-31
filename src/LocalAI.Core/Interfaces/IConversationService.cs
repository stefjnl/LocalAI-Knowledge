using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IConversationService
{
    // Basic conversation management
    Task<List<ChatConversationSummary>> GetAllConversationsAsync();
    Task<ChatConversation?> GetConversationAsync(Guid id);
    Task<ChatConversation> CreateConversationAsync(string title = "New Chat");
    Task<ChatConversation> UpdateConversationAsync(ChatConversation conversation);
    Task<bool> DeleteConversationAsync(Guid id);
    Task<ConversationMessage> AddMessageAsync(Guid conversationId, string role, string content);
    Task<List<ConversationMessage>> GetMessagesAsync(Guid conversationId);

    // Enhanced conversation features
    Task<ConversationSummary?> GenerateConversationSummaryAsync(Guid conversationId);
    Task<List<string>> GetFollowUpSuggestionsAsync(Guid conversationId, int maxSuggestions = 5);
    Task<ConversationExport> ExportConversationAsync(Guid conversationId, string format = "json");
    Task<ConversationImportResult> ImportConversationAsync(ConversationExport exportData);
    Task<bool> UpdateConversationTitleAsync(Guid conversationId, string newTitle);
    Task<List<ChatConversationSummary>> SearchConversationsAsync(string query, int maxResults = 10);

    // Memory management
    Task<bool> ArchiveConversationAsync(Guid conversationId);
    Task<bool> UnarchiveConversationAsync(Guid conversationId);
    Task<List<ChatConversationSummary>> GetArchivedConversationsAsync();
    Task<bool> CleanupOldConversationsAsync(TimeSpan olderThan);
}
