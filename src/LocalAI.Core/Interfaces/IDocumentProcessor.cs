using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IDocumentProcessor
    {
        Task<List<DocumentChunk>> ProcessAllDocumentsAsync();
        Task<List<DocumentChunk>> ProcessTextFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessPdfFileAsync(string filePath);
    }
}