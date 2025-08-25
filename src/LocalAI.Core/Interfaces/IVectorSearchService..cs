using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IVectorSearchService
    {
        Task<bool> CollectionExistsAsync(string collectionName);
        Task StoreDocumentsAsync(IEnumerable<DocumentChunk> chunks);
        Task<List<SearchResult>> SearchAsync(string query, int limit = 5);
        Task<bool> DeleteDocumentAsync(string documentName);
        
        // Debugging methods
        Task<List<SearchResult>> RawSearchAsync(string query, int limit = 20);
        Task<List<DocumentChunkInfo>> GetDocumentChunksAsync(string sourceFilename);
    }
    
    // Supporting models for debugging
    public class DocumentChunkInfo
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string PageInfo { get; set; } = string.Empty;
    }
}