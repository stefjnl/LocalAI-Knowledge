using System.Net.Http;
using System.Text.Json;
using LocalAI.Core.Models;

namespace LocalAI.Web.Services;

public interface IApiService
{
    Task<ApiResponse<SearchResponse>> SearchAsync(string query, int limit = 8, List<ConversationExchange>? context = null);
    Task<ApiResponse<ProcessDocumentsResponse>> ProcessDocumentsAsync();
    Task<ApiResponse<ProcessDocumentsResponse>> ProcessNewDocumentsAsync();
    Task<ApiResponse<bool>> DeleteDocumentAsync(string documentName);
    Task<ApiResponse<CollectionStatusResponse>> GetCollectionStatusAsync();
    Task<ApiResponse<UploadDocumentResponse>> UploadDocumentAsync(string fileName, string fileType, byte[] fileContent);
    Task<ApiResponse<UploadDocumentResponse>> FetchUrlAsync(string url);
    Task<ApiResponse<ProcessedDocumentsResponse>> GetProcessedDocumentsAsync();
    Task<ApiResponse<LastRunResponse>> GetLastRunAsync();
    Task<ApiResponse<ProcessingSummaryResponse>> GetProcessingSummaryAsync();

    // Debug API methods
    Task<ApiResponse<CollectionStatsResponse>> GetCollectionStatsAsync();
    Task<ApiResponse<RawSearchResponse>> PerformRawSearchAsync(string query);
    Task<ApiResponse<DocumentChunksResponse>> GetDocumentChunksAsync(string filename);

    // Conversation API methods
    Task<ApiResponse<List<ChatConversationSummary>>> GetConversationsAsync();
    Task<ApiResponse<ChatConversation>> GetConversationAsync(Guid id);
    Task<ApiResponse<ChatConversation>> CreateConversationAsync(string title = "New Chat");
    Task<ApiResponse<bool>> DeleteConversationAsync(Guid id);
    Task<ApiResponse<ConversationMessage>> AddMessageToConversationAsync(Guid conversationId, string role, string content);
    Task<ApiResponse<ConversationExport>> ExportConversationAsync(Guid conversationId, string format = "json");
    Task<ApiResponse<ConversationImportResult>> ImportConversationAsync(string importData, string format = "json");
    Task<ApiResponse<CodeResponse>> CodeSearchAsync(string query, List<ConversationExchange>? context = null);
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

    public async Task<ApiResponse<SearchResponse>> SearchAsync(string query, int limit = 8, List<LocalAI.Core.Models.ConversationExchange>? context = null)
    {
        try
        {
            // Log context being sent
            Console.WriteLine($"[DEBUG] ApiService sending request: {query}");
            if (context != null && context.Any())
            {
                Console.WriteLine($"[DEBUG] ApiService sending conversation history: {context.Count} exchanges");
                for (int i = 0; i < context.Count; i++)
                {
                    var exchange = context[i];
                    Console.WriteLine($"[DEBUG] ApiService Exchange {i + 1} - User: {exchange.Query}");
                    Console.WriteLine($"[DEBUG] ApiService Exchange {i + 1} - Assistant: {exchange.Response}");
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] ApiService sending no conversation history");
            }

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

    public async Task<ApiResponse<ProcessDocumentsResponse>> ProcessNewDocumentsAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/documents/process-new", null);

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

    public async Task<ApiResponse<bool>> DeleteDocumentAsync(string documentName)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/documents/{documentName}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ApiResponse<bool>>(content, _jsonOptions);
                return new ApiResponse<bool> { Success = true, Data = true };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<bool> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<bool> { Success = false, Error = ex.Message };
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

    public async Task<ApiResponse<UploadDocumentResponse>> FetchUrlAsync(string url)
    {
        try
        {
            var request = new { Url = url };
            var response = await _httpClient.PostAsJsonAsync("/api/documents/fetch-url", request);

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

    // Debug API methods
    public async Task<ApiResponse<CollectionStatsResponse>> GetCollectionStatsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/debug/collection-stats");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CollectionStatsResponse>(content, _jsonOptions);
                return new ApiResponse<CollectionStatsResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<CollectionStatsResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<CollectionStatsResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<RawSearchResponse>> PerformRawSearchAsync(string query)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var response = await _httpClient.GetAsync($"/api/debug/search-chunks/{encodedQuery}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<RawSearchResponse>(content, _jsonOptions);
                return new ApiResponse<RawSearchResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<RawSearchResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<RawSearchResponse> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<DocumentChunksResponse>> GetDocumentChunksAsync(string filename)
    {
        try
        {
            var encodedFilename = Uri.EscapeDataString(filename);
            var response = await _httpClient.GetAsync($"/api/debug/document-chunks/{encodedFilename}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<DocumentChunksResponse>(content, _jsonOptions);
                return new ApiResponse<DocumentChunksResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<DocumentChunksResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<DocumentChunksResponse> { Success = false, Error = ex.Message };
        }
    }

    // Conversation API methods
    public async Task<ApiResponse<List<ChatConversationSummary>>> GetConversationsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/conversations");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<List<ChatConversationSummary>>(content, _jsonOptions);
                return new ApiResponse<List<ChatConversationSummary>> { Success = true, Data = result ?? new List<ChatConversationSummary>() };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<List<ChatConversationSummary>> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ChatConversationSummary>> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ChatConversation>> GetConversationAsync(Guid id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/conversations/{id}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ChatConversation>(content, _jsonOptions);
                return new ApiResponse<ChatConversation> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ChatConversation> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ChatConversation> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ChatConversation>> CreateConversationAsync(string title = "New Chat")
    {
        try
        {
            var request = new { Title = title };
            var response = await _httpClient.PostAsJsonAsync("/api/conversations", request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ChatConversation>(content, _jsonOptions);
                return new ApiResponse<ChatConversation> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ChatConversation> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ChatConversation> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<bool>> DeleteConversationAsync(Guid id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/conversations/{id}");

            if (response.IsSuccessStatusCode)
            {
                return new ApiResponse<bool> { Success = true, Data = true };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<bool> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<bool> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ConversationMessage>> AddMessageToConversationAsync(Guid conversationId, string role, string content)
    {
        try
        {
            var request = new { Role = role, Content = content };
            var response = await _httpClient.PostAsJsonAsync($"/api/conversations/{conversationId}/messages", request);

            if (response.IsSuccessStatusCode)
            {
                var contentString = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ConversationMessage>(contentString, _jsonOptions);
                return new ApiResponse<ConversationMessage> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ConversationMessage> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ConversationMessage> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ConversationExport>> ExportConversationAsync(Guid conversationId, string format = "json")
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/conversations/{conversationId}/export?format={format}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ConversationExport>(content, _jsonOptions);
                return new ApiResponse<ConversationExport> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ConversationExport> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ConversationExport> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<ConversationImportResult>> ImportConversationAsync(string importData, string format = "json")
    {
        try
        {
            var content = new StringContent(importData, System.Text.Encoding.UTF8, GetMimeType(format));
            var response = await _httpClient.PostAsync("/api/conversations/import", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ConversationImportResult>(responseContent, _jsonOptions);
                return new ApiResponse<ConversationImportResult> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<ConversationImportResult> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ConversationImportResult> { Success = false, Error = ex.Message };
        }
    }

    public async Task<ApiResponse<CodeResponse>> CodeSearchAsync(string query, List<ConversationExchange>? context = null)
    {
        try
        {
            var request = new CodeRequest(query, context);
            var response = await _httpClient.PostAsJsonAsync("/api/code", request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CodeResponse>(content, _jsonOptions);
                return new ApiResponse<CodeResponse> { Success = true, Data = result };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new ApiResponse<CodeResponse> { Success = false, Error = error };
        }
        catch (Exception ex)
        {
            return new ApiResponse<CodeResponse> { Success = false, Error = ex.Message };
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

    private string GetMimeType(string format)
    {
        return format switch
        {
            "json" => "application/json",
            "md" => "text/markdown",
            "txt" => "text/plain",
            _ => "application/json"
        };
    }
}

// DTOs matching the actual API responses
public record SearchRequest(string Query, int? Limit = 8, List<LocalAI.Core.Models.ConversationExchange>? Context = null);
public record CodeRequest(string Query, List<LocalAI.Core.Models.ConversationExchange>? Context = null);
public record CodeResponse(string Response);

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

// Debug response models
public record CollectionStatsResponse
{
    public bool Success { get; set; }
    public bool CollectionExists { get; set; }
    public string? CollectionInfo { get; set; }
}

public record RawSearchResponse
{
    public bool Success { get; set; }
    public string Query { get; set; } = string.Empty;
    public List<RawSearchResult> Results { get; set; } = new();
}

public record RawSearchResult
{
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public float Score { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PageInfo { get; set; } = string.Empty;
}

public record DocumentChunksResponse
{
    public bool Success { get; set; }
    public string Filename { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public List<DocumentChunkInfo> Chunks { get; set; } = new();
}

public record DocumentChunkInfo
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string PageInfo { get; set; } = string.Empty;
}
