using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Web.Services;

public class ConversationExportService : IConversationExportService
{
    public async Task<ConversationExport> ExportConversationAsync(ChatConversation conversation, string format = "json")
    {
        return new ConversationExport
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
        };
    }

    public async Task<string> ExportToMarkdownAsync(ChatConversation conversation)
    {
        var export = await ExportConversationAsync(conversation, "md");
        return await GenerateMarkdownExport(export);
    }

    public async Task<string> ExportToTextAsync(ChatConversation conversation)
    {
        var export = await ExportConversationAsync(conversation, "txt");
        return await GenerateTextExport(export);
    }

    public async Task<string> ExportToJsonAsync(ChatConversation conversation)
    {
        var export = await ExportConversationAsync(conversation, "json");
        return await GenerateJsonExport(export);
    }

    public async Task<ConversationImportResult> ImportConversationAsync(string importData, string format = "json")
    {
        try
        {
            switch (format.ToLower())
            {
                case "json":
                    return await ImportFromJsonAsync(importData);
                case "md":
                case "markdown":
                    return await ImportFromMarkdownAsync(importData);
                case "txt":
                    return await ImportFromTextAsync(importData);
                default:
                    return new ConversationImportResult
                    {
                        Success = false,
                        Errors = new List<string> { $"Unsupported format: {format}" }
                    };
            }
        }
        catch (Exception ex)
        {
            return new ConversationImportResult
            {
                Success = false,
                Errors = new List<string> { $"Import failed: {ex.Message}" }
            };
        }
    }

    public async Task<ConversationImportResult> ImportFromJsonAsync(string jsonData)
    {
        try
        {
            var export = System.Text.Json.JsonSerializer.Deserialize<ConversationExport>(jsonData);
            if (export == null)
            {
                return new ConversationImportResult
                {
                    Success = false,
                    Errors = new List<string> { "Failed to deserialize JSON data" }
                };
            }

            return new ConversationImportResult
            {
                Success = true,
                MessagesImported = export.Messages.Count
            };
        }
        catch (Exception ex)
        {
            return new ConversationImportResult
            {
                Success = false,
                Errors = new List<string> { $"JSON import failed: {ex.Message}" }
            };
        }
    }

    public async Task<ConversationImportResult> ImportFromMarkdownAsync(string markdownData)
    {
        // Simple markdown parsing - in a real implementation, you would have more robust parsing
        try
        {
            var lines = markdownData.Split('\n');
            var messages = new List<ExportedMessage>();
            var currentRole = "";
            var currentContent = new System.Text.StringBuilder();

            foreach (var line in lines)
            {
                if (line.StartsWith("## User") || line.StartsWith("## Assistant"))
                {
                    // Save previous message if exists
                    if (!string.IsNullOrEmpty(currentRole) && currentContent.Length > 0)
                    {
                        messages.Add(new ExportedMessage
                        {
                            Role = currentRole,
                            Content = currentContent.ToString().Trim(),
                            Timestamp = DateTime.UtcNow
                        });
                        currentContent.Clear();
                    }

                    // Set new role
                    currentRole = line.Contains("User") ? "user" : "assistant";
                }
                else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    currentContent.AppendLine(line);
                }
            }

            // Add last message
            if (!string.IsNullOrEmpty(currentRole) && currentContent.Length > 0)
            {
                messages.Add(new ExportedMessage
                {
                    Role = currentRole,
                    Content = currentContent.ToString().Trim(),
                    Timestamp = DateTime.UtcNow
                });
            }

            return new ConversationImportResult
            {
                Success = true,
                MessagesImported = messages.Count
            };
        }
        catch (Exception ex)
        {
            return new ConversationImportResult
            {
                Success = false,
                Errors = new List<string> { $"Markdown import failed: {ex.Message}" }
            };
        }
    }


    public async Task<ConversationImportResult> ImportFromTextAsync(string textData)
    {
        // Simple text parsing - in a real implementation, you would have more robust parsing
        try
        {
            var lines = textData.Split('\n');
            var messages = new List<ExportedMessage>();
            var currentRole = "";
            var currentContent = new System.Text.StringBuilder();

            foreach (var line in lines)
            {
                if (line.StartsWith("[User]") || line.StartsWith("[Assistant]"))
                {
                    // Save previous message if exists
                    if (!string.IsNullOrEmpty(currentRole) && currentContent.Length > 0)
                    {
                        messages.Add(new ExportedMessage
                        {
                            Role = currentRole,
                            Content = currentContent.ToString().Trim(),
                            Timestamp = DateTime.UtcNow
                        });
                        currentContent.Clear();
                    }

                    // Set new role
                    currentRole = line.Contains("User") ? "user" : "assistant";
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    currentContent.AppendLine(line);
                }
            }

            // Add last message
            if (!string.IsNullOrEmpty(currentRole) && currentContent.Length > 0)
            {
                messages.Add(new ExportedMessage
                {
                    Role = currentRole,
                    Content = currentContent.ToString().Trim(),
                    Timestamp = DateTime.UtcNow
                });
            }

            return new ConversationImportResult
            {
                Success = true,
                MessagesImported = messages.Count
            };
        }
        catch (Exception ex)
        {
            return new ConversationImportResult
            {
                Success = false,
                Errors = new List<string> { $"Text import failed: {ex.Message}" }
            };
        }
    }

    public async Task<bool> ValidateExportDataAsync(string data, string format)
    {
        try
        {
            var importResult = await ImportConversationAsync(data, format);
            return importResult.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GenerateJsonExport(ConversationExport export)
    {
        return System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<string> GenerateMarkdownExport(ConversationExport export)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {export.Title}");
        sb.AppendLine();
        sb.AppendLine($"Exported on: {export.ExportedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Message count: {export.Messages.Count}");
        sb.AppendLine($"Format: {export.Format}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var message in export.Messages)
        {
            var role = message.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"## {role} ({message.Timestamp:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> GenerateTextExport(ConversationExport export)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Conversation: {export.Title}");
        sb.AppendLine($"Exported on: {export.ExportedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Message count: {export.Messages.Count}");
        sb.AppendLine($"Format: {export.Format}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        foreach (var message in export.Messages)
        {
            var role = message.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"[{role}] {message.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
