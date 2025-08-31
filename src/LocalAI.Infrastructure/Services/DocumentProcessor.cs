using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Tesseract;
using HtmlAgilityPack;
using MimeKit;

namespace LocalAI.Infrastructure.Services
{
    public class DocumentProcessor : IDocumentProcessor
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<DocumentProcessor>? _logger;
        private readonly string _transcriptsPath;
        private readonly string _pdfsPath;
        private readonly string _markdownPath;
        private readonly string _imagesPath;
        private readonly string _emailsPath;
        private readonly string _webPagesPath;
        private readonly string _epubPath;
        private readonly string _processedFilesPath;
        private readonly string _processingMetadataPath;

        public DocumentProcessor(IEmbeddingService embeddingService, IConfiguration configuration, ILogger<DocumentProcessor>? logger = null)
        {
            _embeddingService = embeddingService;
            _logger = logger;
            _transcriptsPath = configuration["DocumentPaths:Transcripts"] ?? "data/transcripts/";
            _pdfsPath = configuration["DocumentPaths:PDFs"] ?? "data/pdfs/";

            // New document type paths
            _markdownPath = configuration["DocumentPaths:Markdown"] ?? "data/markdown/";
            _imagesPath = configuration["DocumentPaths:Images"] ?? "data/images/";
            _emailsPath = configuration["DocumentPaths:Emails"] ?? "data/emails/";
            _webPagesPath = configuration["DocumentPaths:WebPages"] ?? "data/webpages/";
            _epubPath = configuration["DocumentPaths:EPUB"] ?? "data/epub/";

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
                _logger?.LogInformation("Using persistent metadata storage: {MetadataPath}", metadataPath);
            }
            catch (Exception ex)
            {
                var tmpPath = Path.GetTempPath();
                _processedFilesPath = Path.Combine(tmpPath, "processed_files.json");
                _processingMetadataPath = Path.Combine(tmpPath, "processing_metadata.json");
                Console.WriteLine($"WARNING: Could not use persistent storage ({ex.Message}). Using temporary storage: {tmpPath}");
                Console.WriteLine("Data will be lost on container restart. Consider mounting a volume to /app/data");
                _logger?.LogWarning(ex, "Could not use persistent storage. Using temporary storage: {TempPath}. Data will be lost on container restart.", tmpPath);
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

            // Process Markdown files
            if (Directory.Exists(_markdownPath))
            {
                var markdownFiles = Directory.GetFiles(_markdownPath, "*.md", SearchOption.AllDirectories);
                foreach (var file in markdownFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed Markdown: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessMarkdownFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Markdown",
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
                        Console.WriteLine($"Error processing Markdown {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Markdown",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process images
            if (Directory.Exists(_imagesPath))
            {
                var imageFiles = Directory.GetFiles(_imagesPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" }.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in imageFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed image: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessImageFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Image",
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
                        Console.WriteLine($"Error processing image {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Image",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process emails
            if (Directory.Exists(_emailsPath))
            {
                var emailFiles = Directory.GetFiles(_emailsPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".eml", ".msg" }.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in emailFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed email: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessEmailFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Email",
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
                        Console.WriteLine($"Error processing email {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Email",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process web pages
            if (Directory.Exists(_webPagesPath))
            {
                var webPageFiles = Directory.GetFiles(_webPagesPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".html", ".htm", ".mhtml" }.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in webPageFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed web page: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessWebPageFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "WebPage",
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
                        Console.WriteLine($"Error processing web page {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "WebPage",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process EPUB files
            if (Directory.Exists(_epubPath))
            {
                var epubFiles = Directory.GetFiles(_epubPath, "*.epub", SearchOption.AllDirectories);
                foreach (var file in epubFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed EPUB: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessEpubFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "EPUB",
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
                        Console.WriteLine($"Error processing EPUB {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "EPUB",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
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

            // Process only new Markdown files
            if (Directory.Exists(_markdownPath))
            {
                var markdownFiles = Directory.GetFiles(_markdownPath, "*.md", SearchOption.AllDirectories);
                foreach (var file in markdownFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed Markdown: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessMarkdownFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Markdown",
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
                        Console.WriteLine($"Error processing Markdown {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Markdown",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process only new images
            if (Directory.Exists(_imagesPath))
            {
                var imageFiles = Directory.GetFiles(_imagesPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" }.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in imageFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed image: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessImageFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Image",
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
                        Console.WriteLine($"Error processing image {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Image",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process only new emails
            if (Directory.Exists(_emailsPath))
            {
                var emailFiles = Directory.GetFiles(_emailsPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".eml", ".msg" }.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in emailFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed email: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessEmailFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Email",
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
                        Console.WriteLine($"Error processing email {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "Email",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process only new web pages
            if (Directory.Exists(_webPagesPath))
            {
                var webPageFiles = Directory.GetFiles(_webPagesPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".html", ".htm", ".mhtml" }.Contains(Path.GetExtension(f).ToLower()));
                foreach (var file in webPageFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed web page: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessWebPageFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "WebPage",
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
                        Console.WriteLine($"Error processing web page {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "WebPage",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
                    }
                }
            }

            // Process only new EPUB files
            if (Directory.Exists(_epubPath))
            {
                var epubFiles = Directory.GetFiles(_epubPath, "*.epub", SearchOption.AllDirectories);
                foreach (var file in epubFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (processedFiles.Contains(fileName))
                    {
                        Console.WriteLine($"Skipping already processed EPUB: {fileName}");
                        continue;
                    }

                    var fileStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var chunks = await ProcessEpubFileAsync(file);
                        fileStopwatch.Stop();

                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);

                        // Track metadata for this file
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "EPUB",
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
                        Console.WriteLine($"Error processing EPUB {file}: {ex.Message}");

                        // Track failed processing
                        var metadata = new ProcessingMetadata
                        {
                            FileName = fileName,
                            DocumentType = "EPUB",
                            ChunksProcessed = 0,
                            ProcessingDurationMs = fileStopwatch.ElapsedMilliseconds,
                            ProcessedAt = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        currentRunMetadata.Add(metadata);
                        SaveFileMetadata(metadata);
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
            var fileName = Path.GetFileName(filePath);
            _logger?.LogInformation("Starting PDF processing for file: {FileName}", fileName);

            var pdfContent = ExtractTextFromPdf(filePath);
            var textChunks = SplitIntoChunks(pdfContent.text, 600, 50);
            var chunks = new List<DocumentChunk>();

            _logger?.LogInformation("Extracted {ChunkCount} chunks from PDF: {FileName}", textChunks.Count, fileName);

            foreach (var (chunk, index) in textChunks.Select((c, i) => (c, i)))
            {
                _logger?.LogDebug("Processing chunk {ChunkIndex}/{TotalChunks} for file: {FileName}", index + 1, textChunks.Count, fileName);

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

                // Log progress every 100 chunks
                if ((index + 1) % 100 == 0)
                {
                    _logger?.LogInformation("Processed {ChunkCount} chunks for file: {FileName}", index + 1, fileName);
                }
            }

            _logger?.LogInformation("Completed PDF processing for file: {FileName}. Total chunks: {ChunkCount}", fileName, chunks.Count);

            return chunks;
        }

        public async Task<List<DocumentChunk>> ProcessMarkdownFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger?.LogInformation("Starting Markdown processing for file: {FileName}", fileName);

            var content = await File.ReadAllTextAsync(filePath);
            var processedContent = ProcessMarkdownContent(content);
            var textChunks = SplitIntoChunks(processedContent, 600, 50);
            var chunks = new List<DocumentChunk>();

            _logger?.LogInformation("Extracted {ChunkCount} chunks from Markdown: {FileName}", textChunks.Count, fileName);

            foreach (var chunk in textChunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = "markdown"
                });
            }

            _logger?.LogInformation("Completed Markdown processing for file: {FileName}. Total chunks: {ChunkCount}", fileName, chunks.Count);

            return chunks;
        }

        public async Task<List<DocumentChunk>> ProcessImageFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger?.LogInformation("Starting image processing with OCR for file: {FileName}", fileName);

            var extractedText = ExtractTextFromImage(filePath);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger?.LogWarning("No text extracted from image: {FileName}", fileName);
                return new List<DocumentChunk>();
            }

            var textChunks = SplitIntoChunks(extractedText, 600, 50);
            var chunks = new List<DocumentChunk>();

            _logger?.LogInformation("Extracted {ChunkCount} chunks from image: {FileName}", textChunks.Count, fileName);

            foreach (var chunk in textChunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = "image|ocr"
                });
            }

            _logger?.LogInformation("Completed image processing for file: {FileName}. Total chunks: {ChunkCount}", fileName, chunks.Count);

            return chunks;
        }

        public async Task<List<DocumentChunk>> ProcessEmailFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger?.LogInformation("Starting email processing for file: {FileName}", fileName);

            var emailContent = ExtractTextFromEmail(filePath);
            var textChunks = SplitIntoChunks(emailContent, 600, 50);
            var chunks = new List<DocumentChunk>();

            _logger?.LogInformation("Extracted {ChunkCount} chunks from email: {FileName}", textChunks.Count, fileName);

            foreach (var chunk in textChunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = "email"
                });
            }

            _logger?.LogInformation("Completed email processing for file: {FileName}. Total chunks: {ChunkCount}", fileName, chunks.Count);

            return chunks;
        }

        public async Task<List<DocumentChunk>> ProcessWebPageFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger?.LogInformation("Starting web page processing for file: {FileName}", fileName);

            var webContent = ExtractTextFromWebPage(filePath);
            var textChunks = SplitIntoChunks(webContent, 600, 50);
            var chunks = new List<DocumentChunk>();

            _logger?.LogInformation("Extracted {ChunkCount} chunks from web page: {FileName}", textChunks.Count, fileName);

            foreach (var chunk in textChunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = "webpage"
                });
            }

            _logger?.LogInformation("Completed web page processing for file: {FileName}. Total chunks: {ChunkCount}", fileName, chunks.Count);

            return chunks;
        }

        public async Task<List<DocumentChunk>> ProcessEpubFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger?.LogInformation("Starting EPUB processing for file: {FileName}", fileName);

            var epubContent = ExtractTextFromEpub(filePath);
            var textChunks = SplitIntoChunks(epubContent, 600, 50);
            var chunks = new List<DocumentChunk>();

            _logger?.LogInformation("Extracted {ChunkCount} chunks from EPUB: {FileName}", textChunks.Count, fileName);

            foreach (var chunk in textChunks)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                var source = Path.GetFileNameWithoutExtension(filePath);

                chunks.Add(new DocumentChunk
                {
                    Text = chunk,
                    Embedding = embedding,
                    Source = source,
                    Metadata = "epub"
                });
            }

            _logger?.LogInformation("Completed EPUB processing for file: {FileName}. Total chunks: {ChunkCount}", fileName, chunks.Count);

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

        // Helper methods for new document types
        private static string ProcessMarkdownContent(string content)
        {
            // Simple markdown processing - preserve headings and structure
            // For more advanced processing, could use a markdown parser
            return content
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim();
        }

        private static string ExtractTextFromImage(string filePath)
        {
            try
            {
                using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
                {
                    using (var img = Pix.LoadFromFile(filePath))
                    {
                        using (var page = engine.Process(img))
                        {
                            return page.GetText();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR processing failed for {filePath}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractTextFromEmail(string filePath)
        {
            try
            {
                var message = MimeMessage.Load(filePath);

                var content = new System.Text.StringBuilder();

                // Add email headers
                content.AppendLine($"From: {message.From}");
                content.AppendLine($"To: {message.To}");
                content.AppendLine($"Subject: {message.Subject}");
                content.AppendLine($"Date: {message.Date}");
                content.AppendLine();

                // Add body content
                if (message.TextBody != null)
                {
                    content.AppendLine(message.TextBody);
                }

                if (message.HtmlBody != null)
                {
                    // Simple HTML to text conversion
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(message.HtmlBody);
                    var text = htmlDoc.DocumentNode.InnerText;
                    content.AppendLine(text);
                }

                return content.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email processing failed for {filePath}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractTextFromWebPage(string filePath)
        {
            try
            {
                var htmlContent = File.ReadAllText(filePath);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(htmlContent);

                // Remove script and style elements
                htmlDoc.DocumentNode.Descendants()
                    .Where(n => n.Name == "script" || n.Name == "style")
                    .ToList()
                    .ForEach(n => n.Remove());

                // Extract text content
                var text = htmlDoc.DocumentNode.InnerText;

                // Clean up whitespace
                return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Web page processing failed for {filePath}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractTextFromEpub(string filePath)
        {
            // TODO: Implement EPUB processing when package is available
            // For now, return empty string
            Console.WriteLine($"EPUB processing not yet implemented for {filePath}");
            return string.Empty;
        }
    }
}
