using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LocalAI.Infrastructure.Services
{
    public class DocumentProcessor : IDocumentProcessor
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly string _transcriptsPath;
        private readonly string _pdfsPath;
        private readonly string _processedFilesPath;
        private readonly string _processingMetadataPath;

        public DocumentProcessor(IEmbeddingService embeddingService, IConfiguration configuration)
        {
            _embeddingService = embeddingService;
            _transcriptsPath = configuration["DocumentPaths:Transcripts"] ?? "data/transcripts/";
            _pdfsPath = configuration["DocumentPaths:PDFs"] ?? "data/pdfs/";

            // Try to use persistent data directory, fallback to /tmp for Docker compatibility
            var metadataPath = configuration["DocumentPaths:Metadata"]
                ?? Environment.GetEnvironmentVariable("METADATA_PATH")
                ?? "data/metadata";

            // If metadata path doesn't exist and we can't create it, fallback to /tmp
            try
            {
                Directory.CreateDirectory(metadataPath);
                _processedFilesPath = Path.Combine(metadataPath, "processed_files.json");
                _processingMetadataPath = Path.Combine(metadataPath, "processing_metadata.json");
                Console.WriteLine($"Using persistent metadata storage: {metadataPath}");
            }
            catch (Exception ex)
            {
                var tmpPath = Path.GetTempPath();
                _processedFilesPath = Path.Combine(tmpPath, "processed_files.json");
                _processingMetadataPath = Path.Combine(tmpPath, "processing_metadata.json");
                Console.WriteLine($"WARNING: Could not use persistent storage ({ex.Message}). Using temporary storage: {tmpPath}");
                Console.WriteLine("Data will be lost on container restart. Consider mounting a volume to /app/data");
            }
        }

        public async Task<List<DocumentChunk>> ProcessAllDocumentsAsync()
        {
            var allChunks = new List<DocumentChunk>();
            var processedFiles = LoadProcessedFiles();
            var currentRunMetadata = new List<ProcessingMetadata>();
            var stopwatch = Stopwatch.StartNew();

            // Process transcripts
            if (Directory.Exists(_transcriptsPath))
            {
                var transcriptFiles = Directory.GetFiles(_transcriptsPath, "*.txt");
                foreach (var file in transcriptFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed transcript: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    var chunks = await ProcessTextFileAsync(file);
                    fileStopwatch.Stop();

                    allChunks.AddRange(chunks);
                    processedFiles.Add(fileName);

                    // Track metadata for this file
                    var metadata = new ProcessingMetadata
                    {
                        FileName = fileName,
                        DocumentType = "Transcript",
                        ChunksProcessed = chunks.Count,
                        ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                        ProcessedAt = DateTime.UtcNow,
                        Success = true
                    };
                    currentRunMetadata.Add(metadata);
                    SaveFileMetadata(metadata);
                }
            }

            // Process PDFs
            if (Directory.Exists(_pdfsPath))
            {
                var pdfFiles = Directory.GetFiles(_pdfsPath, "*.pdf", SearchOption.AllDirectories);
                foreach (var file in pdfFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed PDF: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessPdfFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "PDF",
                            ChunksProcessed = chunks.Count,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = true
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                    catch (Exception ex)
                    {
                        fileStopwatch.Stop();
                        Console.WriteLine($"Error processing PDF {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "PDF",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                        // Continue with next file instead of failing completely
                    }
                }
            }

            stopwatch.Stop();
            SaveProcessedFiles(processedFiles);
            SaveLastRunMetadata(currentRunMetadata, stopwatch.ElapsedMilliseconds);

            return allChunks;
        }

        public async Task<List<DocumentChunk>> ProcessNewDocumentsAsync()
        {
            var allChunks = new List<DocumentChunk>();
            var processedFiles = LoadProcessedFiles();
            var currentRunMetadata = new List<ProcessingMetadata>();
            var stopwatch = Stopwatch.StartNew();

            // Process only new transcripts
            if (Directory.Exists(_transcriptsPath))
            {
                var transcriptFiles = Directory.GetFiles(_transcriptsPath, "*.txt");
                foreach (var file in transcriptFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed transcript: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    var chunks = await ProcessTextFileAsync(file);
                    fileStopwatch.Stop();

                    allChunks.AddRange(chunks);
                    processedFiles.Add(fileName);

                    // Track metadata for this file
                    var metadata = new ProcessingMetadata
                    {
                        FileName = fileName,
                        DocumentType = "Transcript",
                        ChunksProcessed = chunks.Count,
                        ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                        ProcessedAt = DateTime.UtcNow,
                        Success = true
                    };
                    currentRunMetadata.Add(metadata);
                    SaveFileMetadata(metadata);
                }
            }

            // Process only new PDFs
            if (Directory.Exists(_pdfsPath))
            {
                var pdfFiles = Directory.GetFiles(_pdfsPath, "*.pdf", SearchOption.AllDirectories);
                foreach (var file in pdfFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed PDF: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessPdfFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "PDF",
                            ChunksProcessed = chunks.Count,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = true
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                    catch (Exception ex)
                    {
                        fileStopwatch.Stop();
                        Console.WriteLine($"Error processing PDF {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "PDF",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                        // Continue with next file instead of failing completely
                    }
                }
            }

            stopwatch.Stop();
            SaveProcessedFiles(processedFiles);
            SaveLastRunMetadata(currentRunMetadata, stopwatch.ElapsedMilliseconds);

            return allChunks;
        }

        public List<string> GetProcessedFiles()
        {
            return LoadProcessedFiles().ToList();
        }

        public List<ProcessingMetadata> GetAllProcessedDocumentsMetadata()
        {
            return LoadAllProcessingMetadata();
        }

        public List<ProcessingMetadata> GetLastRunMetadata()
        {
            return LoadLastRunMetadata();
        }

        public ProcessingRunSummary GetProcessingSummary()
        {
            var allMetadata = LoadAllProcessingMetadata();
            var lastRunMetadata = LoadLastRunMetadata();

            return new ProcessingRunSummary
            {
                TotalDocuments = allMetadata.Count,
                TotalChunks = allMetadata.Sum(m => m.ChunksProcessed),
                SuccessfulDocuments = allMetadata.Count(m => m.Success),
                FailedDocuments = allMetadata.Count(m => !m.Success),
                LastRunDocuments = lastRunMetadata.Count,
                LastRunChunks = lastRunMetadata.Sum(m => m.ChunksProcessed),
                AllDocuments = allMetadata,
                LastRunDetails = lastRunMetadata
            };
        }

        // Public method to save file metadata (used by upload endpoint)
        public void SaveFileMetadata(ProcessingMetadata metadata)
        {
            var allMetadata = LoadAllProcessingMetadata();

            // Remove existing entry for this file if it exists
            allMetadata.RemoveAll(m => m.FileName == metadata.FileName);

            // Add the new metadata
            allMetadata.Add(metadata);

            SaveAllProcessingMetadata(allMetadata);
        }

        // Public method to delete file metadata
        public void DeleteFileMetadata(string fileName)
        {
            var allMetadata = LoadAllProcessingMetadata();
            
            // Remove entry for this file
            allMetadata.RemoveAll(m => m.FileName == fileName);
            
            SaveAllProcessingMetadata(allMetadata);
            
            // Also remove from processed files list
            var processedFiles = LoadProcessedFiles();
            processedFiles.Remove(fileName);
            SaveProcessedFiles(processedFiles);
        }

        private HashSet<string> LoadProcessedFiles()
        {
            if (File.Exists(_processedFilesPath))
            {
                var json = File.ReadAllText(_processedFilesPath);
                var files = JsonSerializer.Deserialize<HashSet<string>>(json);
                return files ?? new HashSet<string>();
            }
            return new HashSet<string>();
        }

        private void SaveProcessedFiles(HashSet<string> processedFiles)
        {
            var json = JsonSerializer.Serialize(processedFiles, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_processedFilesPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(_processedFilesPath, json);
        }

        private void SaveLastRunMetadata(List<ProcessingMetadata> runMetadata, long totalDurationMs)
        {
            var lastRun = new LastProcessingRun
            {
                ProcessedAt = DateTime.UtcNow,
                TotalDurationMs = totalDurationMs,
                DocumentsProcessed = runMetadata.Count,
                TotalChunks = runMetadata.Sum(m => m.ChunksProcessed),
                Documents = runMetadata
            };

            var json = JsonSerializer.Serialize(lastRun, new JsonSerializerOptions { WriteIndented = true });
            var lastRunPath = Path.Combine(Path.GetDirectoryName(_processingMetadataPath) ?? "", "last_processing_run.json");
            File.WriteAllText(lastRunPath, json);
        }

        private List<ProcessingMetadata> LoadAllProcessingMetadata()
        {
            if (File.Exists(_processingMetadataPath))
            {
                var json = File.ReadAllText(_processingMetadataPath);
                var metadata = JsonSerializer.Deserialize<List<ProcessingMetadata>>(json);
                return metadata ?? new List<ProcessingMetadata>();
            }
            return new List<ProcessingMetadata>();
        }

        private void SaveAllProcessingMetadata(List<ProcessingMetadata> metadata)
        {
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_processingMetadataPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(_processingMetadataPath, json);
        }

        private List<ProcessingMetadata> LoadLastRunMetadata()
        {
            var lastRunPath = Path.Combine(Path.GetDirectoryName(_processingMetadataPath) ?? "", "last_processing_run.json");
            if (File.Exists(lastRunPath))
            {
                var json = File.ReadAllText(lastRunPath);
                var lastRun = JsonSerializer.Deserialize<LastProcessingRun>(json);
                return lastRun?.Documents ?? new List<ProcessingMetadata>();
            }
            return new List<ProcessingMetadata>();
        }

        // Existing methods remain unchanged for backward compatibility
        public async Task<List<DocumentChunk>> ProcessTextFileAsync(string filePath)
        {
            var content = await File.ReadAllTextAsync(filePath);
            var textChunks = SplitIntoChunks(content, 500, 50);
            var chunks = new List<DocumentChunk>();

            foreach (var chunk in textChunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = "transcript"
                });
            }

            return chunks;
        }

        public async Task<List<DocumentChunk>> ProcessPdfFileAsync(string filePath)
        {
            var pdfContent = ExtractTextFromPdf(filePath);
            var textChunks = SplitIntoChunks(pdfContent.text, 600, 50);
            var chunks = new List<DocumentChunk>();

            foreach (var (chunk, index) in textChunks.Select((c, i) => (c, i)))
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);
                var pageInfo = GetChunkPageInfo(pdfContent.pageBreaks, index, textChunks.Count, chunk, pdfContent.text);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = $"pdf|{pageInfo}"
                });
            }

            return chunks;
        }

        // All existing private methods remain unchanged...
        private static (string text, List<(int pageNum, int charStart)> pageBreaks) ExtractTextFromPdf(string filePath)
        {
            var fullText = "";
            var pageBreaks = new List<(int pageNum, int charStart)>();

            using (var document = PdfDocument.Open(filePath))
            {
                foreach (var page in document.GetPages())
                {
                    pageBreaks.Add((page.Number, fullText.Length));

                    var pageText = ContentOrderTextExtractor.GetText(page);
                    pageText = CleanPdfText(pageText);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        fullText += pageText + "\n\n";
                    }
                }
            }

            return (fullText.Trim(), pageBreaks);
        }

        private static string CleanPdfText(string text)
        {
            return text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\u00A0", " ")
                .Replace("\u2010", "-")
                .Replace("\u2013", "-")
                .Replace("\u2014", "-")
                .Replace("\u201C", "\"")
                .Replace("\u201D", "\"")
                .Replace("\u2018", "'")
                .Replace("\u2019", "'")
                .Trim();
        }

        private static string GetChunkPageInfo(List<(int pageNum, int charStart)> pageBreaks, int chunkIndex, int totalChunks, string chunkText, string fullText)
        {
            var chunkStart = fullText.IndexOf(chunkText);

            if (chunkStart == -1)
            {
                var estimatedPage = Math.Min(chunkIndex * pageBreaks.Count / totalChunks + 1, pageBreaks.Count);
                return $"Page {estimatedPage}";
            }

            var pageNum = 1;
            for (int i = 0; i < pageBreaks.Count; i++)
            {
                if (chunkStart >= pageBreaks[i].charStart)
                {
                    pageNum = pageBreaks[i].pageNum;
                }
                else
                {
                    break;
                }
            }

            return $"Page {pageNum}";
        }

        private static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            var chunks = new List<string>();
            var sentences = SplitIntoSentences(text);
            var currentChunk = "";

            foreach (var sentence in sentences)
            {
                var testChunk = string.IsNullOrEmpty(currentChunk) ? sentence : currentChunk + " " + sentence;

                if (testChunk.Length <= chunkSize)
                {
                    currentChunk = testChunk;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(currentChunk))
                    {
                        chunks.Add(currentChunk.Trim());
                    }

                    currentChunk = sentence;

                    if (sentence.Length > chunkSize)
                    {
                        var longSentenceParts = SplitLongSentence(sentence, chunkSize);
                        chunks.AddRange(longSentenceParts.Take(longSentenceParts.Count - 1));
                        currentChunk = longSentenceParts.Last();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }

            return chunks;
        }

        private static List<string> SplitIntoSentences(string text)
        {
            var sentences = new List<string>();
            var current = "";

            for (int i = 0; i < text.Length; i++)
            {
                current += text[i];

                if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                    i + 1 < text.Length &&
                    char.IsWhiteSpace(text[i + 1]))
                {
                    sentences.Add(current.Trim());
                    current = "";
                }
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                sentences.Add(current.Trim());
            }

            return sentences.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private static List<string> SplitLongSentence(string sentence, int maxLength)
        {
            var parts = new List<string>();
            var words = sentence.Split(' ');
            var currentPart = "";

            foreach (var word in words)
            {
                var testPart = string.IsNullOrEmpty(currentPart) ? word : currentPart + " " + word;

                if (testPart.Length <= maxLength)
                {
                    currentPart = testPart;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(currentPart))
                    {
                        parts.Add(currentPart + "...");
                        currentPart = "..." + word;
                    }
                    else
                    {
                        parts.Add(word);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentPart))
            {
                parts.Add(currentPart);
            }

            return parts;
        }
    }
}
