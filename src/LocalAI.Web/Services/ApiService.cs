using System.Text.Json;
using LocalAI.Core.Models;

namespace LocalAI.Web.Services;

public interface IApiService
{
    Task<ApiResponse<SearchResponse>> SearchAsync(string query, int limit = 8);
    Task<ApiResponse<ProcessDocumentsResponse>> ProcessDocumentsAsync();
    Task<ApiResponse<CollectionStatusResponse>> GetCollectionStatusAsync();
}

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(configuration["ApiSettings:BaseUrl"] ?? "https://localhost:7190");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ApiResponse<SearchResponse>> SearchAsync(string query, int limit = 8)
    {
        try
        {
            var request = new SearchRequest(query, limit);
            var response = await _httpClient.PostAsJsonAsync("/api/search", request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<SearchResponse>(content, _jsonOptions);
                return new ApiResponse<SearchResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<SearchResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<SearchResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ProcessDocumentsResponse>> ProcessDocumentsAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/documents/process", null);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ProcessDocumentsResponse>(content, _jsonOptions);
                return new ApiResponse<ProcessDocumentsResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ProcessDocumentsResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ProcessDocumentsResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<CollectionStatusResponse>> GetCollectionStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/collection/exists");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CollectionStatusResponse>(content, _jsonOptions);
                return new ApiResponse<CollectionStatusResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<CollectionStatusResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<CollectionStatusResponse> { Success = false, Error = ex.Message };
        }
    }
}

// DTOs matching your API
public record SearchRequest(string Query, int? Limit = 8);

public record SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool HasResults { get; set; }
    public string RAGResponse { get; set; } = string.Empty;
    public List<SearchResult> Sources { get; set; } = new();
}

public record ProcessDocumentsResponse
{
    public bool Success { get; set; }
    public int ChunksProcessed { get; set; }
    public string Message { get; set; } = string.Empty;
}

public record CollectionStatusResponse
{
    public bool CollectionExists { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Error { get; set; } = string.Empty;
}