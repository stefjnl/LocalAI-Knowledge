using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IConversationExportService
{
    Task<ConversationExport> ExportConversationAsync(ChatConversation conversation, string format = "json");
    Task<string> ExportToMarkdownAsync(ChatConversation conversation);
    Task<string> ExportToTextAsync(ChatConversation conversation);
    Task<string> ExportToJsonAsync(ChatConversation conversation);
    Task<ConversationImportResult> ImportConversationAsync(string importData, string format = "json");
    Task<ConversationImportResult> ImportFromJsonAsync(string jsonData);
    Task<ConversationImportResult> ImportFromMarkdownAsync(string markdownData);
    Task<ConversationImportResult> ImportFromTextAsync(string textData);
    Task<bool> ValidateExportDataAsync(string data, string format);
}
