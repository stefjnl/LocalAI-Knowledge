using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using System.Text;
using System.Text.Json;

namespace LocalAI.Infrastructure.Services;

public class ConversationExportService : IConversationExportService
{
    public async Task<ConversationExport> ExportConversationAsync(ChatConversation conversation, string format = "json")
    {
        return format.ToLower() switch
        {
            "markdown" => new ConversationExport
            {
                ConversationId = conversation.Id,
                Title = conversation.Title,
                ExportedAt = DateTime.UtcNow,
                Format = format,
                Messages = conversation.Messages.Select(m => new ExportedMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    Metadata = m.Metadata
                }).ToList(),
                Summary = conversation.Summary,
                Metadata = new Dictionary<string, object>
                {
                    { "messageCount", conversation.Messages.Count },
                    { "createdAt", conversation.CreatedAt },
                    { "updatedAt", conversation.UpdatedAt }
                }
            },
            "txt" => new ConversationExport
            {
                ConversationId = conversation.Id,
                Title = conversation.Title,
                ExportedAt = DateTime.UtcNow,
                Format = format,
                Messages = conversation.Messages.Select(m => new ExportedMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    Metadata = m.Metadata
                }).ToList(),
                Summary = conversation.Summary,
                Metadata = new Dictionary<string, object>
                {
                    { "messageCount", conversation.Messages.Count },
                    { "createdAt", conversation.CreatedAt },
                    { "updatedAt", conversation.UpdatedAt }
                }
            },
            _ => new ConversationExport
            {
                ConversationId = conversation.Id,
                Title = conversation.Title,
                ExportedAt = DateTime.UtcNow,
                Format = format,
                Messages = conversation.Messages.Select(m => new ExportedMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    Metadata = m.Metadata
                }).ToList(),
                Summary = conversation.Summary,
                Metadata = new Dictionary<string, object>
                {
                    { "messageCount", conversation.Messages.Count },
                    { "createdAt", conversation.CreatedAt },
                    { "updatedAt", conversation.UpdatedAt }
                }
            }
        };
    }

    public async Task<string> ExportToMarkdownAsync(ChatConversation conversation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {conversation.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Exported:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Message Count:** {conversation.Messages.Count}");
        sb.AppendLine();

        if (conversation.Summary != null)
        {
            sb.AppendLine("## Summary");
            sb.AppendLine(conversation.Summary.Content);
            sb.AppendLine();
        }

        sb.AppendLine("## Conversation");
        sb.AppendLine();

        foreach (var message in conversation.Messages)
        {
            var role = message.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"### {role} ({message.Timestamp:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> ExportToTextAsync(ChatConversation conversation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Conversation: {conversation.Title}");
        sb.AppendLine($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Message Count: {conversation.Messages.Count}");
        sb.AppendLine();

        if (conversation.Summary != null)
        {
            sb.AppendLine("Summary:");
            sb.AppendLine(conversation.Summary.Content);
            sb.AppendLine();
        }

        sb.AppendLine("Conversation:");
        sb.AppendLine();

        foreach (var message in conversation.Messages)
        {
            var role = message.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"[{message.Timestamp:yyyy-MM-dd HH:mm:ss}] {role}:");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> ExportToJsonAsync(ChatConversation conversation)
    {
        var exportData = await ExportConversationAsync(conversation, "json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        return JsonSerializer.Serialize(exportData, options);
    }

    public async Task<ConversationImportResult> ImportConversationAsync(string importData, string format = "json")
    {
        return format.ToLower() switch
        {
            "json" => await ImportFromJsonAsync(importData),
            "markdown" => await ImportFromMarkdownAsync(importData),
            _ => new ConversationImportResult
            {
                Success = false,
                ConversationId = null,
                MessagesImported = 0,
                Errors = new List<string> { $"Unsupported format: {format}" }
            }
        };
    }

    public async Task<ConversationImportResult> ImportFromJsonAsync(string jsonData)
    {
        try
        {
            var exportData = JsonSerializer.Deserialize<ConversationExport>(jsonData);
            if (exportData == null)
            {
                return new ConversationImportResult
                {
                    Success = false,
                    ConversationId = null,
                    MessagesImported = 0,
                    Errors = new List<string> { "Failed to deserialize JSON data" }
                };
            }

            return new ConversationImportResult
            {
                Success = true,
                ConversationId = exportData.ConversationId,
                MessagesImported = exportData.Messages.Count,
                Errors = new List<string>()
            };
        }
        catch (Exception ex)
        {
            return new ConversationImportResult
            {
                Success = false,
                ConversationId = null,
                MessagesImported = 0,
                Errors = new List<string> { $"Error importing from JSON: {ex.Message}" }
            };
        }
    }

    public async Task<ConversationImportResult> ImportFromMarkdownAsync(string markdownData)
    {
        // Simple markdown import - in a real implementation, you would parse the markdown structure
        return new ConversationImportResult
        {
            Success = true,
            ConversationId = Guid.NewGuid(),
            MessagesImported = 0,
            Errors = new List<string> { "Markdown import is not fully implemented" }
        };
    }

    public async Task<ConversationImportResult> ImportFromTextAsync(string textData)
    {
        // Simple text import - in a real implementation, you would parse the text structure
        return new ConversationImportResult
        {
            Success = true,
            ConversationId = Guid.NewGuid(),
            MessagesImported = 0,
            Errors = new List<string> { "Text import is not fully implemented" }
        };
    }

    public async Task<bool> ValidateExportDataAsync(string data, string format)
    {
        try
        {
            return format.ToLower() switch
            {
                "json" => ValidateJsonExportData(data),
                "markdown" => ValidateMarkdownExportData(data),
                "txt" => ValidateTextExportData(data),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private bool ValidateJsonExportData(string jsonData)
    {
        try
        {
            var exportData = JsonSerializer.Deserialize<ConversationExport>(jsonData);
            return exportData != null;
        }
        catch
        {
            return false;
        }
    }

    private bool ValidateMarkdownExportData(string markdownData)
    {
        // Simple validation - check if it contains conversation data
        return !string.IsNullOrWhiteSpace(markdownData) && markdownData.Contains("#");
    }

    private bool ValidateTextExportData(string textData)
    {
        // Simple validation - check if it contains conversation data
        return !string.IsNullOrWhiteSpace(textData);
    }
}
