using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LocalAI.Infrastructure.Services;

public class FileBasedConversationService : IConversationService
{
    private readonly string _conversationsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileBasedConversationService()
    {
        // Use a data directory for conversations
        _conversationsDirectory = Path.Combine("data", "conversations");
        Directory.CreateDirectory(_conversationsDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<List<ChatConversationSummary>> GetAllConversationsAsync()
    {
        var conversations = new List<ChatConversationSummary>();
        var files = Directory.GetFiles(_conversationsDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var conversation = JsonSerializer.Deserialize<ChatConversation>(json, _jsonOptions);
                if (conversation != null)
                {
                    conversations.Add(new ChatConversationSummary
                    {
                        Id = conversation.Id,
                        Title = conversation.Title,
                        CreatedAt = conversation.CreatedAt,
                        UpdatedAt = conversation.UpdatedAt
                    });
                }
            }
            catch
            {
                // Skip corrupted files
                continue;
            }
        }

        // Sort by updated date (newest first)
        return conversations.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public async Task<ChatConversation?> GetConversationAsync(Guid id)
    {
        var filePath = Path.Combine(_conversationsDirectory, $"{id}.json");
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<ChatConversation>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ChatConversation> CreateConversationAsync(string title = "New Chat")
    {
        var conversation = new ChatConversation
        {
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await SaveConversationAsync(conversation);
        return conversation;
    }

    public async Task<ChatConversation> UpdateConversationAsync(ChatConversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        await SaveConversationAsync(conversation);
        return conversation;
    }

    public async Task<bool> DeleteConversationAsync(Guid id)
    {
        var filePath = Path.Combine(_conversationsDirectory, $"{id}.json");
        if (!File.Exists(filePath))
            return false;

        try
        {
            File.Delete(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ConversationMessage> AddMessageAsync(Guid conversationId, string role, string content)
    {
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            throw new ArgumentException("Conversation not found", nameof(conversationId));
        }

        var message = new ConversationMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        conversation.Messages.Add(message);
        conversation.UpdatedAt = DateTime.UtcNow;

        await SaveConversationAsync(conversation);
        return message;
    }

    public async Task<List<ConversationMessage>> GetMessagesAsync(Guid conversationId)
    {
        var conversation = await GetConversationAsync(conversationId);
        return conversation?.Messages ?? new List<ConversationMessage>();
    }

    // Enhanced conversation features
    public async Task<ConversationSummary?> GenerateConversationSummaryAsync(Guid conversationId)
    {
        // This would typically use a summarization service
        // For now, return null to indicate no summary available
        return null;
    }

    public async Task<List<string>> GetFollowUpSuggestionsAsync(Guid conversationId, int maxSuggestions = 5)
    {
        // Return empty list for now
        return new List<string>();
    }

    public async Task<ConversationExport> ExportConversationAsync(Guid conversationId, string format = "json")
    {
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            throw new ArgumentException("Conversation not found", nameof(conversationId));
        }

        var exportedMessages = conversation.Messages.Select(m => new ExportedMessage
        {
            Role = m.Role,
            Content = m.Content,
            Timestamp = m.Timestamp,
            Metadata = m.Metadata
        }).ToList();

        return new ConversationExport
        {
            ConversationId = conversation.Id,
            Title = conversation.Title,
            ExportedAt = DateTime.UtcNow,
            Format = format,
            Messages = exportedMessages,
            Summary = conversation.Summary,
            Metadata = new Dictionary<string, object>
            {
                { "messageCount", conversation.Messages.Count },
                { "createdAt", conversation.CreatedAt },
                { "updatedAt", conversation.UpdatedAt }
            }
        };
    }

    public async Task<ConversationImportResult> ImportConversationAsync(ConversationExport exportData)
    {
        try
        {
            var conversation = new ChatConversation
            {
                Id = exportData.ConversationId,
                Title = exportData.Title,
                CreatedAt = exportData.ExportedAt,
                UpdatedAt = exportData.ExportedAt,
                Summary = exportData.Summary
            };

            foreach (var exportedMessage in exportData.Messages)
            {
                var message = new ConversationMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Role = exportedMessage.Role,
                    Content = exportedMessage.Content,
                    Timestamp = exportedMessage.Timestamp,
                    Metadata = exportedMessage.Metadata ?? new MessageMetadata()
                };
                conversation.Messages.Add(message);
            }

            await SaveConversationAsync(conversation);

            return new ConversationImportResult
            {
                Success = true,
                ConversationId = conversation.Id,
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
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<bool> UpdateConversationTitleAsync(Guid conversationId, string newTitle)
    {
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            return false;
        }

        conversation.Title = newTitle;
        conversation.UpdatedAt = DateTime.UtcNow;
        await SaveConversationAsync(conversation);
        return true;
    }

    public async Task<List<ChatConversationSummary>> SearchConversationsAsync(string query, int maxResults = 10)
    {
        var allConversations = await GetAllConversationsAsync();
        var matchingConversations = allConversations
            .Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       (c.Summary?.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(maxResults)
            .ToList();

        return matchingConversations;
    }

    // Memory management
    public async Task<bool> ArchiveConversationAsync(Guid conversationId)
    {
        // For file-based storage, we'll just add an archived flag to metadata
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            return false;
        }

        conversation.Metadata.CustomProperties["archived"] = true;
        conversation.UpdatedAt = DateTime.UtcNow;
        await SaveConversationAsync(conversation);
        return true;
    }

    public async Task<bool> UnarchiveConversationAsync(Guid conversationId)
    {
        // For file-based storage, we'll just remove the archived flag from metadata
        var conversation = await GetConversationAsync(conversationId);
        if (conversation == null)
        {
            return false;
        }

        conversation.Metadata.CustomProperties.Remove("archived");
        conversation.UpdatedAt = DateTime.UtcNow;
        await SaveConversationAsync(conversation);
        return true;
    }

    public async Task<List<ChatConversationSummary>> GetArchivedConversationsAsync()
    {
        // For file-based storage, we need to check the actual conversation files for archived status
        var archivedConversations = new List<ChatConversationSummary>();
        var files = Directory.GetFiles(_conversationsDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var conversation = JsonSerializer.Deserialize<ChatConversation>(json, _jsonOptions);
                if (conversation != null &&
                    conversation.Metadata?.CustomProperties?.ContainsKey("archived") == true &&
                    conversation.Metadata.CustomProperties["archived"] is bool archived && archived)
                {
                    archivedConversations.Add(new ChatConversationSummary
                    {
                        Id = conversation.Id,
                        Title = conversation.Title,
                        CreatedAt = conversation.CreatedAt,
                        UpdatedAt = conversation.UpdatedAt
                    });
                }
            }
            catch
            {
                // Skip corrupted files
                continue;
            }
        }

        return archivedConversations;
    }

    public async Task<bool> CleanupOldConversationsAsync(TimeSpan olderThan)
    {
        var cutoffDate = DateTime.UtcNow.Subtract(olderThan);
        var allConversations = await GetAllConversationsAsync();
        var oldConversations = allConversations
            .Where(c => c.UpdatedAt < cutoffDate)
            .ToList();

        var success = true;
        foreach (var conversation in oldConversations)
        {
            var filePath = Path.Combine(_conversationsDirectory, $"{conversation.Id}.json");
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                success = false;
                // Continue with other conversations even if one fails
            }
        }

        return success;
    }

    private async Task SaveConversationAsync(ChatConversation conversation)
    {
        var filePath = Path.Combine(_conversationsDirectory, $"{conversation.Id}.json");
        var json = JsonSerializer.Serialize(conversation, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }
}
