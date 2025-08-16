using Microsoft.JSInterop;
using static LocalAI.Web.Services.ApiService;

namespace LocalAI.Web.Services;

public interface IConversationService
{
    Task<List<ConversationHistoryItem>> GetConversationHistoryAsync();
    Task AddToConversationHistoryAsync(string query, string response);
    Task ClearConversationHistoryAsync();
    Task<List<ConversationExchange>> GetRecentContextAsync(int count = 3);
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
                return new List<ConversationHistoryItem>();
            }

            var history = System.Text.Json.JsonSerializer.Deserialize<List<ConversationHistoryItem>>(json);
            return history ?? new List<ConversationHistoryItem>();
        }
        catch
        {
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
        }
        catch
        {
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

    public async Task<List<ConversationExchange>> GetRecentContextAsync(int count = 3)
    {
        var history = await GetConversationHistoryAsync();
        var recentItems = history.TakeLast(count).ToList();

        // Convert to ConversationExchange (without timestamp)
        return recentItems.Select(item => new ConversationExchange
        {
            Query = item.Query,
            Response = item.Response
        }).ToList();
    }
}
