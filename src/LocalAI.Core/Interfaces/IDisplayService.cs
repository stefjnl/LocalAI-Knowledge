using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IDisplayService
    {
        void DisplaySearchResults(List<SearchResult> results, string query);
        void DisplayRAGResponse(string query, string response, List<SearchResult> sources);
        void DisplayProgress(string message);
        void DisplayError(string error);
    }
}