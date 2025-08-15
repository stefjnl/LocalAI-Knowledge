using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.Extensions.Configuration;
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

        public DocumentProcessor(IEmbeddingService embeddingService, IConfiguration configuration)
        {
            _embeddingService = embeddingService;
            _transcriptsPath = configuration["DocumentPaths:Transcripts"] ?? "data/transcripts/";
            _pdfsPath = configuration["DocumentPaths:PDFs"] ?? "data/pdfs/";

            // Use /tmp directory for processed files to ensure writability in Docker containers
            var tmpPath = Path.GetTempPath();
            _processedFilesPath = Path.Combine(tmpPath, "processed_files.json");
        }

        public async Task<List<DocumentChunk>> ProcessAllDocumentsAsync()
        {
            var allChunks = new List<DocumentChunk>();
            var processedFiles = LoadProcessedFiles();

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

                    var chunks = await ProcessTextFileAsync(file);
                    allChunks.AddRange(chunks);
                    processedFiles.Add(fileName);
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

                    try
                    {
                        var chunks = await ProcessPdfFileAsync(file);
                        allChunks.AddRange(chunks);
                        processedFiles.Add(fileName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing PDF {file}: {ex.Message}");
                        // Continue with next file instead of failing completely
                    }
                }
            }

            SaveProcessedFiles(processedFiles);
            return allChunks;
        }

        public List<string> GetProcessedFiles()
        {
            return LoadProcessedFiles().ToList();
        }

        private HashSet<string> LoadProcessedFiles()
        {
            if (File.Exists(_processedFilesPath))
            {
                var json = File.ReadAllText(_processedFilesPath);
                var files = System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(json);
                return files ?? new HashSet<string>();
            }
            return new HashSet<string>();
        }

        private void SaveProcessedFiles(HashSet<string> processedFiles)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(processedFiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_processedFilesPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(_processedFilesPath, json);
        }

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

        private static (string text, List<(int pageNum, int charStart)> pageBreaks) ExtractTextFromPdf(string filePath)
        {
            var fullText = "";
            var pageBreaks = new List<(int pageNum, int charStart)>();

            using (var document = PdfDocument.Open(filePath))
            {
                foreach (var page in document.GetPages())
                {
                    pageBreaks.Add((page.Number, fullText.Length));

                    // Use simple page.Text for faster extraction (like old Pdfium approach)
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
                .Replace("\u00A0", " ")  // Non-breaking space
                .Replace("\u2010", "-") // Hyphen
                .Replace("\u2013", "-") // En dash
                .Replace("\u2014", "-") // Em dash
                .Replace("\u201C", "\"") // Left double quotation mark
                .Replace("\u201D", "\"") // Right double quotation mark
                .Replace("\u2018", "'")  // Left single quotation mark
                .Replace("\u2019", "'")  // Right single quotation mark
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
