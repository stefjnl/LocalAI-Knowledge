using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IRAGService
    {
        Task<string> GenerateResponseAsync(string query, List<SearchResult> searchResults);
    }
}