using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces
{
    public interface IDocumentProcessor
    {
        Task<List<DocumentChunk>> ProcessAllDocumentsAsync();
        Task<List<DocumentChunk>> ProcessNewDocumentsAsync();
        Task<List<DocumentChunk>> ProcessTextFileAsync(string filePath);
        Task<List<DocumentChunk>> ProcessPdfFileAsync(string filePath);

        // New methods for enhanced metadata tracking
        List<string> GetProcessedFiles();
        List<ProcessingMetadata> GetAllProcessedDocumentsMetadata();
        List<ProcessingMetadata> GetLastRunMetadata();
        ProcessingRunSummary GetProcessingSummary();
        void SaveFileMetadata(ProcessingMetadata metadata);
        void DeleteFileMetadata(string fileName);
    }
}