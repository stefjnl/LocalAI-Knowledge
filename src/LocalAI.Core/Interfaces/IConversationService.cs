using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IConversationService
{
    Task<List<ChatConversationSummary>> GetAllConversationsAsync();
    Task<ChatConversation?> GetConversationAsync(Guid id);
    Task<ChatConversation> CreateConversationAsync(string title = "New Chat");
    Task<ChatConversation> UpdateConversationAsync(ChatConversation conversation);
    Task<bool> DeleteConversationAsync(Guid id);
    Task<ConversationMessage> AddMessageAsync(Guid conversationId, string role, string content);
    Task<List<ConversationMessage>> GetMessagesAsync(Guid conversationId);
}