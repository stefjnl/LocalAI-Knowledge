using LocalAI.Core.Models;
using Microsoft.JSInterop;
using static LocalAI.Web.Services.ApiService;

namespace LocalAI.Web.Services;

public interface IConversationService
{
    Task<List<ConversationHistoryItem>> GetConversationHistoryAsync();
    Task AddToConversationHistoryAsync(string query, string response);
    Task ClearConversationHistoryAsync();
    Task<List<LocalAI.Core.Models.ConversationExchange>> GetRecentContextAsync(int count = 3);
}

public class ConversationHistoryItem
{
    public string Query { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ConversationService : IConversationService
{
    private readonly IJSRuntime _jsRuntime;
    private const string StorageKey = "conversationHistory";

    public ConversationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<List<ConversationHistoryItem>> GetConversationHistoryAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json))
            {
                Console.WriteLine("[DEBUG] No conversation history found in sessionStorage");
                return new List<ConversationHistoryItem>();
            }

            var history = System.Text.Json.JsonSerializer.Deserialize<List<ConversationHistoryItem>>(json);
            var result = history ?? new List<ConversationHistoryItem>();
            
            // Log the history being retrieved for debugging
            Console.WriteLine($"[DEBUG] Retrieved conversation history with {result.Count} exchanges");
            for (int i = 0; i < result.Count; i++)
            {
                var item = result[i];
                Console.WriteLine($"[DEBUG] History Exchange {i + 1} - User: {item.Query}");
                Console.WriteLine($"[DEBUG] History Exchange {i + 1} - Assistant: {item.Response}");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // Log the error for debugging
            Console.WriteLine($"[DEBUG] Error retrieving conversation history: {ex.Message}");
            return new List<ConversationHistoryItem>();
        }
    }

    public async Task AddToConversationHistoryAsync(string query, string response)
    {
        try
        {
            var history = await GetConversationHistoryAsync();

            // Add new exchange
            history.Add(new ConversationHistoryItem
            {
                Query = query,
                Response = response,
                Timestamp = DateTime.UtcNow
            });

            // Keep only the last 20 exchanges to prevent storage from growing too large
            if (history.Count > 20)
            {
                history = history.Skip(history.Count - 20).ToList();
            }

            var json = System.Text.Json.JsonSerializer.Serialize(history);
            await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", StorageKey, json);
            
            // Log the addition for debugging
            Console.WriteLine($"[DEBUG] Added conversation exchange - User: {query}");
            Console.WriteLine($"[DEBUG] Added conversation exchange - Assistant: {response}");
            Console.WriteLine($"[DEBUG] Conversation history now contains {history.Count} exchanges");
        }
        catch (Exception ex)
        {
            // Log the error for debugging
            Console.WriteLine($"[DEBUG] Error adding to conversation history: {ex.Message}");
            // Silently fail if sessionStorage is not available
        }
    }

    public async Task ClearConversationHistoryAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        }
        catch
        {
            // Silently fail if sessionStorage is not available
        }
    }

    public async Task<List<LocalAI.Core.Models.ConversationExchange>> GetRecentContextAsync(int count = 3)
    {
        var history = await GetConversationHistoryAsync();
        var recentItems = history.TakeLast(count).ToList();

        // Log the context being retrieved for debugging
        Console.WriteLine($"[DEBUG] Retrieved conversation context with {recentItems.Count} exchanges");
        for (int i = 0; i < recentItems.Count; i++)
        {
            var item = recentItems[i];
            Console.WriteLine($"[DEBUG] Context Exchange {i + 1} - User: {item.Query}");
            Console.WriteLine($"[DEBUG] Context Exchange {i + 1} - Assistant: {item.Response}");
        }

        // Convert to ConversationExchange (without timestamp)
        return recentItems.Select(item => new LocalAI.Core.Models.ConversationExchange
        {
            Query = item.Query,
            Response = item.Response
        }).ToList();
    }
}
