using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using System.Text.Json;

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

    private async Task SaveConversationAsync(ChatConversation conversation)
    {
        var filePath = Path.Combine(_conversationsDirectory, $"{conversation.Id}.json");
        var json = JsonSerializer.Serialize(conversation, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }
}