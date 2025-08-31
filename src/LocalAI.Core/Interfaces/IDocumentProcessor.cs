using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IDocumentProcessor
    {
        Task<List<DocumentChunk>> ProcessAllDocumentsAsync();
        Task<List<DocumentChunk>> ProcessNewDocumentsAsync();
        Task<List<DocumentChunk>> ProcessTextFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessPdfFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessMarkdownFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessImageFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessEmailFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessWebPageFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessEpubFileAsync(string filePath);

        // New methods for enhanced metadata tracking
        List<string> GetProcessedFiles();
        List<ProcessingMetadata> GetAllProcessedDocumentsMetadata();
        List<ProcessingMetadata> GetLastRunMetadata();
        ProcessingRunSummary GetProcessingSummary();
        void SaveFileMetadata(ProcessingMetadata metadata);
        void DeleteFileMetadata(string fileName);
    }
}
