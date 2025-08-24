using System.Net.Http;
using System.Text.Json;
using LocalAI.Core.Models;

namespace LocalAI.Web.Services;

public interface IChatApiService
{
    Task<ApiResponse<List<ChatSessionResponse>>> GetSessionsAsync();
    Task<ApiResponse<ChatSessionResponse>> CreateSessionAsync();
    Task<ApiResponse<ChatSessionResponse>> GetSessionAsync(Guid sessionId);
    Task<ApiResponse<List<ChatMessageResponse>>> GetSessionMessagesAsync(Guid sessionId);
    Task<ApiResponse<ChatMessageResponse>> AddMessageAsync(Guid sessionId, string content, MessageRole role);
    Task<ApiResponse<bool>> DeleteSessionAsync(Guid sessionId);
}

public class ChatApiService : IChatApiService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ChatApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(configuration["ApiSettings:BaseUrl"] ?? "http://localai-api:8080");
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<ApiResponse<List<ChatSessionResponse>>> GetSessionsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/sessions");
            return await HandleResponse<List<ChatSessionResponse>>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ChatSessionResponse>> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ChatSessionResponse>> CreateSessionAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/sessions", null);
            return await HandleResponse<ChatSessionResponse>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<ChatSessionResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ChatSessionResponse>> GetSessionAsync(Guid sessionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/sessions/{sessionId}");
            return await HandleResponse<ChatSessionResponse>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<ChatSessionResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<List<ChatMessageResponse>>> GetSessionMessagesAsync(Guid sessionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/sessions/{sessionId}/messages");
            return await HandleResponse<List<ChatMessageResponse>>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ChatMessageResponse>> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ChatMessageResponse>> AddMessageAsync(Guid sessionId, string content, MessageRole role)
    {
        try
        {
            var messageRequest = new { Content = content, Role = role.ToString() };
            var response = await _httpClient.PostAsJsonAsync($"/api/sessions/{sessionId}/messages", messageRequest);
            return await HandleResponse<ChatMessageResponse>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<ChatMessageResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<bool>> DeleteSessionAsync(Guid sessionId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/sessions/{sessionId}");
            return await HandleResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<bool> { Success = false, Error = ex.Message };
        }
    }

    private async Task<ApiResponse<T>> HandleResponse<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<T>(content, _jsonOptions);
            return new ApiResponse<T> { Success = true, Data = result };
        }

        var error = await response.Content.ReadAsStringAsync();
        return new ApiResponse<T> { Success = false, Error = error };
    }
}
