namespace LocalAI.Web.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
}

public class SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool HasResults { get; set; }
    public string RAGResponse { get; set; } = string.Empty;
    public List<LocalAI.Core.Models.SearchResult> Sources { get; set; } = new();
}

public class CollectionStatus
{
    public bool CollectionExists { get; set; }
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public int ChunksProcessed { get; set; }
    public string Message { get; set; } = string.Empty;
}