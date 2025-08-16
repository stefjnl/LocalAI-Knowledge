using System.Net.Http;
using System.Text.Json;
using LocalAI.Core.Models;

namespace LocalAI.Web.Services;

public interface IApiService
{
    Task<ApiResponse<SearchResponse>> SearchAsync(string query, int limit = 8, List<ConversationExchange>? context = null);
    Task<ApiResponse<ProcessDocumentsResponse>> ProcessDocumentsAsync();
    Task<ApiResponse<CollectionStatusResponse>> GetCollectionStatusAsync();
    Task<ApiResponse<UploadDocumentResponse>> UploadDocumentAsync(string fileName, string fileType, byte[] fileContent);
    Task<ApiResponse<ProcessedDocumentsResponse>> GetProcessedDocumentsAsync();
    Task<ApiResponse<LastRunResponse>> GetLastRunAsync();
    Task<ApiResponse<ProcessingSummaryResponse>> GetProcessingSummaryAsync();
}

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(configuration["ApiSettings:BaseUrl"] ?? "http://localai-api:8080");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ApiResponse<SearchResponse>> SearchAsync(string query, int limit = 8, List<ConversationExchange>? context = null)
    {
        try
        {
            var request = new SearchRequest(query, limit, context);
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

    public async Task<ApiResponse<UploadDocumentResponse>> UploadDocumentAsync(string fileName, string fileType, byte[] fileContent)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var fileStream = new MemoryStream(fileContent);
            using var fileContentStream = new StreamContent(fileStream);

            fileContentStream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetContentType(fileName));
            content.Add(fileContentStream, "file", fileName);
            content.Add(new StringContent(fileType), "documentType");

            var response = await _httpClient.PostAsync("/api/documents/upload", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<UploadDocumentResponse>(responseContent, _jsonOptions);
                return new ApiResponse<UploadDocumentResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<UploadDocumentResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<UploadDocumentResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ProcessedDocumentsResponse>> GetProcessedDocumentsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/documents/processed");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ProcessedDocumentsResponse>(content, _jsonOptions);
                return new ApiResponse<ProcessedDocumentsResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ProcessedDocumentsResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ProcessedDocumentsResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<LastRunResponse>> GetLastRunAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/documents/last-run");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<LastRunResponse>(content, _jsonOptions);
                return new ApiResponse<LastRunResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<LastRunResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<LastRunResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ProcessingSummaryResponse>> GetProcessingSummaryAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/documents/summary");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ProcessingSummaryResponse>(content, _jsonOptions);
                return new ApiResponse<ProcessingSummaryResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ProcessingSummaryResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ProcessingSummaryResponse> { Success = false, Error = ex.Message };
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}

// DTOs matching the actual API responses
public record SearchRequest(string Query, int? Limit = 8, List<ConversationExchange>? Context = null);

public record ConversationExchange
{
    public string Query { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
}

public record TimingInfo
{
    public double TotalTimeMs { get; set; }
    public double SearchTimeMs { get; set; }
    public double GenerationTimeMs { get; set; }
    public string FormattedResponseTime { get; set; } = string.Empty;
}

public record SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool HasResults { get; set; }
    public string RAGResponse { get; set; } = string.Empty;
    public List<SearchResult> Sources { get; set; } = new();
    public TimingInfo Timing { get; set; } = new();
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

public record UploadDocumentResponse
{
    public bool Success { get; set; }
    public int ChunksProcessed { get; set; }
    public string ProcessingDuration { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

// Updated DTOs to match the enhanced API endpoints
public record ProcessedDocumentsResponse
{
    public bool Success { get; set; }
    public ProcessedDocumentInfo[] ProcessedFiles { get; set; } = Array.Empty<ProcessedDocumentInfo>();
    public int TotalCount { get; set; }
    public int TotalChunks { get; set; }
    public int SuccessfulDocuments { get; set; }
    public int FailedDocuments { get; set; }
}

public record ProcessedDocumentInfo
{
    public string FileName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public int ChunksProcessed { get; set; }
    public string ProcessingDuration { get; set; } = string.Empty;
    public string ProcessedAt { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public record LastRunResponse
{
    public bool Success { get; set; }
    public int DocumentsProcessed { get; set; }
    public int TotalChunks { get; set; }
    public ProcessedDocumentInfo[] Documents { get; set; } = Array.Empty<ProcessedDocumentInfo>();
}

public record ProcessingSummaryResponse
{
    public bool Success { get; set; }
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public int SuccessfulDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public int LastRunDocuments { get; set; }
    public int LastRunChunks { get; set; }
    public ProcessedDocumentInfo[] LastRunDetails { get; set; } = Array.Empty<ProcessedDocumentInfo>();
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Error { get; set; } = string.Empty;
}
