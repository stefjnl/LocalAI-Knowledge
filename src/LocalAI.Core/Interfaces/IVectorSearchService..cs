using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IVectorSearchService
    {
        Task<bool> CollectionExistsAsync(string collectionName);
        Task StoreDocumentsAsync(IEnumerable<DocumentChunk> chunks);
        Task<List<SearchResult>> SearchAsync(string query, int limit = 5);
        Task<bool> DeleteDocumentAsync(string documentName);
    }
}