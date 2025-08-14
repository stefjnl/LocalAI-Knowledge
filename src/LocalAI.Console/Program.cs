using DotNetEnv;
using OpenAI;
using System.Text.Json;
using Patagames.Pdf.Net;

// Initialize Pdfium SDK - REQUIRED before any PDF operations
PdfCommon.Initialize();

// Load environment variables
Env.Load();

var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(openAiApiKey))
{
    Console.WriteLine("❌ OPENAI_API_KEY not found in .env file");
    return;
}

var httpClient = new HttpClient();
var qdrantUrl = "http://localhost:6333";

try
{
    Console.WriteLine("🚀 Starting knowledge processing pipeline...");

    // Create directories if they don't exist
    Directory.CreateDirectory("data/transcripts");
    Directory.CreateDirectory("data/pdfs");
    Directory.CreateDirectory("data/pdfs/llms");

    // Check if collection already exists and has data
    if (await CollectionExists(qdrantUrl, "knowledge"))
    {
        Console.WriteLine("✅ Knowledge base already exists, skipping processing");
        Console.WriteLine("🔍 Ready for interactive search!");
        Console.WriteLine("Ask questions about your documents (or 'quit' to exit):");
    }
    else
    {
        Console.WriteLine("📚 Processing all documents for the first time...");
        await ProcessAllDocuments(openAiApiKey, qdrantUrl);

        Console.WriteLine("\n🔍 Ready for interactive search!");
        Console.WriteLine("Ask questions about your documents (or 'quit' to exit):");
    }

    while (true)
    {
        Console.Write("\n> ");
        var userQuery = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userQuery) || userQuery.ToLower() == "quit")
            break;

        try
        {
            await SearchKnowledge(userQuery, openAiApiKey, qdrantUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Search error: {ex.Message}");
        }
    }

}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();

// Helper methods
static async Task ProcessAllDocuments(string apiKey, string qdrantUrl)
{
    var allEmbeddings = new List<(string text, float[] embedding, string source, string metadata)>();

    // Process transcripts
    var transcriptsPath = "data/transcripts/";
    if (Directory.Exists(transcriptsPath))
    {
        var transcriptFiles = Directory.GetFiles(transcriptsPath, "*.txt");
        foreach (var file in transcriptFiles)
        {
            Console.WriteLine($"📖 Processing transcript: {Path.GetFileName(file)}");
            var content = await File.ReadAllTextAsync(file);
            var chunks = SplitIntoChunks(content, 500, 50);

            foreach (var chunk in chunks)
            {
                var embedding = await GetEmbedding(chunk, apiKey);
                var source = Path.GetFileNameWithoutExtension(file);
                allEmbeddings.Add((chunk, embedding, source, "transcript"));
            }

            Console.WriteLine($"✅ Processed {chunks.Count} chunks from {Path.GetFileName(file)}");
        }
    }

    // Process PDFs
    var pdfsPath = "data/pdfs/";
    if (Directory.Exists(pdfsPath))
    {
        var pdfFiles = Directory.GetFiles(pdfsPath, "*.pdf", SearchOption.AllDirectories);
        foreach (var file in pdfFiles)
        {
            Console.WriteLine($"📄 Processing PDF: {Path.GetFileName(file)}");

            try
            {
                var pdfContent = ExtractTextFromPdf(file);
                var chunks = SplitIntoChunks(pdfContent.text, 600, 50); // Slightly larger for PDFs

                foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
                {
                    var embedding = await GetEmbedding(chunk, apiKey);
                    var source = Path.GetFileNameWithoutExtension(file);
                    var pageInfo = GetChunkPageInfo(pdfContent.pageBreaks, index, chunks.Count, chunk, pdfContent.text);

                    allEmbeddings.Add((chunk, embedding, source, $"pdf|{pageInfo}"));
                }

                Console.WriteLine($"✅ Processed {chunks.Count} chunks from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing PDF {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    if (allEmbeddings.Count > 0)
    {
        Console.WriteLine($"💾 Storing {allEmbeddings.Count} total chunks in Qdrant...");
        await StoreAllInQdrant(allEmbeddings, qdrantUrl);
    }
    else
    {
        Console.WriteLine("❌ No documents found to process");
    }
}

static (string text, List<(int pageNum, int charStart)> pageBreaks) ExtractTextFromPdf(string filePath)
{
    var fullText = "";
    var pageBreaks = new List<(int pageNum, int charStart)>();

    // Load the PDF document - Pdfium.Net.SDK syntax
    using (var doc = PdfDocument.Load(filePath))
    {
        for (int pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            // Record where this page starts in the full text
            pageBreaks.Add((pageIndex + 1, fullText.Length));

            using (var page = doc.Pages[pageIndex])
            {
                // Get character count for this page
                int charCount = page.Text.CountChars;

                if (charCount > 0)
                {
                    // Extract all text from the page
                    var pageText = page.Text.GetText(0, charCount);

                    // Clean up PDF text extraction artifacts
                    pageText = CleanPdfText(pageText);

                    fullText += pageText + "\n\n";
                }
            }
        }
    }

    return (fullText.Trim(), pageBreaks);
}

static string CleanPdfText(string text)
{
    return text
        .Replace("\r\n", "\n")
        .Replace("\r", "\n")
        .Replace("\u00A0", " ") // Non-breaking space
        .Replace("\u2010", "-") // Hyphen variants
        .Replace("\u2013", "-") // En dash
        .Replace("\u2014", "-") // Em dash
        .Replace("\u201C", "\"") // Left double quote
        .Replace("\u201D", "\"") // Right double quote
        .Replace("\u2018", "'") // Left single quote
        .Replace("\u2019", "'") // Right single quote
        .Trim();
}

static string GetChunkPageInfo(List<(int pageNum, int charStart)> pageBreaks, int chunkIndex, int totalChunks, string chunkText, string fullText)
{
    // Find the character position of this chunk in the full text
    var chunkStart = fullText.IndexOf(chunkText);

    if (chunkStart == -1)
    {
        // Fallback to estimation if we can't find the chunk
        var estimatedPage = Math.Min(chunkIndex * pageBreaks.Count / totalChunks + 1, pageBreaks.Count);
        return $"Page {estimatedPage}";
    }

    // Find which page this chunk belongs to
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

static async Task StoreAllInQdrant(List<(string text, float[] embedding, string source, string metadata)> embeddings, string qdrantUrl)
{
    using var client = new HttpClient();

    // Create collection first
    var collectionPayload = JsonSerializer.Serialize(new
    {
        vectors = new { size = 768, distance = "Cosine" }
    });

    var createContent = new StringContent(collectionPayload, System.Text.Encoding.UTF8, "application/json");
    await client.PutAsync($"{qdrantUrl}/collections/knowledge", createContent);

    // Store points in batches
    const int batchSize = 100;
    for (int i = 0; i < embeddings.Count; i += batchSize)
    {
        var batch = embeddings.Skip(i).Take(batchSize);
        var points = batch.Select((item, index) => new
        {
            id = i + index,
            vector = item.embedding,
            payload = new
            {
                text = item.text,
                source = item.source,
                type = item.metadata.Split('|')[0], // "transcript" or "pdf"
                page_info = item.metadata.Contains('|') ? item.metadata.Split('|')[1] : ""
            }
        }).ToArray();

        var pointsPayload = JsonSerializer.Serialize(new { points });
        var pointsContent = new StringContent(pointsPayload, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PutAsync($"{qdrantUrl}/collections/knowledge/points", pointsContent);
        if (response.IsSuccessStatusCode)
        {
            Console.Write(".");
        }
        else
        {
            Console.WriteLine($"❌ Batch {i / batchSize + 1} failed");
        }
    }

    Console.WriteLine($"\n✅ Stored {embeddings.Count} points in Qdrant");
}

static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
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
            // Current chunk is full, save it and start new one
            if (!string.IsNullOrWhiteSpace(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }

            // Start new chunk with this sentence
            currentChunk = sentence;

            // If single sentence is too long, split it carefully
            if (sentence.Length > chunkSize)
            {
                var longSentenceParts = SplitLongSentence(sentence, chunkSize);
                chunks.AddRange(longSentenceParts.Take(longSentenceParts.Count - 1));
                currentChunk = longSentenceParts.Last();
            }
        }
    }

    // Add the final chunk
    if (!string.IsNullOrWhiteSpace(currentChunk))
    {
        chunks.Add(currentChunk.Trim());
    }

    return chunks;
}

static async Task<float[]> GetEmbedding(string text, string apiKey)
{
    using var client = new HttpClient();

    // Add search_document prefix for storing documents
    var prefixedText = $"search_document: {text}";

    var payload = JsonSerializer.Serialize(new
    {
        input = prefixedText,
        model = "text-embedding-nomic-embed-text-v2-moe"
    });

    var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
    var response = await client.PostAsync("http://localhost:1234/v1/embeddings", content);
    var responseContent = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"❌ LM Studio API Error: {responseContent}");
        throw new Exception($"LM Studio API failed: {response.StatusCode}");
    }

    try
    {
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var embeddingArray = result.GetProperty("data")[0].GetProperty("embedding").EnumerateArray();
        return embeddingArray.Select(x => (float)x.GetDouble()).ToArray();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error parsing response: {responseContent}");
        throw new Exception($"Failed to parse LM Studio response: {ex.Message}");
    }
}

static async Task SearchKnowledge(string query, string apiKey, string qdrantUrl)
{
    // Get embedding for search query
    var queryEmbedding = await GetEmbedding($"search_query: {query}", apiKey);

    // Search in Qdrant
    using var client = new HttpClient();
    var searchPayload = JsonSerializer.Serialize(new
    {
        vector = queryEmbedding,
        limit = 5, // Increased to show more results from different sources
        with_payload = true
    });

    var searchContent = new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json");
    var response = await client.PostAsync($"{qdrantUrl}/collections/knowledge/points/search", searchContent);
    var responseContent = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"❌ Search error: {responseContent}");
        return;
    }

    // Parse and display results nicely
    try
    {
        var searchResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var results = searchResult.GetProperty("result").EnumerateArray().ToList();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Found {results.Count} relevant passages for: \"{query}\"");
        Console.ResetColor();
        Console.WriteLine();

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var score = result.GetProperty("score").GetSingle();
            var payload = result.GetProperty("payload");
            var text = payload.GetProperty("text").GetString();
            var source = payload.GetProperty("source").GetString();

            // Handle new metadata fields
            var type = payload.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "transcript";
            var pageInfo = payload.TryGetProperty("page_info", out var pageElement) ? pageElement.GetString() : "";

            // Source header with score
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"📄 {GetSourceDisplayName(source, type, pageInfo)}");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" (Score: {score:F2})");
            Console.ResetColor();

            // Content with proper formatting
            var formattedContent = FormatContent(text);
            Console.WriteLine($"\"{formattedContent}\"");

            if (i < results.Count - 1)
            {
                Console.WriteLine();
            }
        }

        Console.WriteLine();
        GenerateFollowUpQuestions(query);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error parsing search results: {ex.Message}");
        Console.WriteLine($"Raw response: {responseContent}");
    }
}

static string GetSourceDisplayName(string source, string type, string pageInfo)
{
    if (type == "pdf")
    {
        var displayName = source.Replace("-", " ").Replace("_", " ");
        return string.IsNullOrEmpty(pageInfo) ? $"{displayName}.pdf" : $"{displayName}.pdf ({pageInfo})";
    }
    else if (type == "transcript")
    {
        return $"{source.Replace("-", " ")} Transcript";
    }

    return source;
}

static string FormatContent(string content)
{
    // Clean up content for better readability
    return content
        .Replace("\n\n", " ")  // Remove double line breaks
        .Replace("\n", " ")    // Replace single line breaks with spaces
        .Trim()
        .Replace("  ", " ");   // Remove double spaces
}

static void GenerateFollowUpQuestions(string originalQuery)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("💡 You might also want to ask:");
    Console.ResetColor();

    var questions = new List<string>();

    // Simple rule-based follow-up generation
    if (originalQuery.ToLower().Contains("design"))
    {
        questions.Add("What are specific examples of good design?");
        questions.Add("How do you measure design quality?");
    }
    else if (originalQuery.ToLower().Contains("complexity"))
    {
        questions.Add("How do you manage complexity in large systems?");
        questions.Add("What causes unnecessary complexity?");
    }
    else if (originalQuery.ToLower().Contains("software"))
    {
        questions.Add("What are the key principles of software engineering?");
        questions.Add("How do you write maintainable code?");
    }
    else
    {
        // Generic fallbacks
        questions.Add($"Can you give examples related to this topic?");
        questions.Add($"What are the main challenges here?");
    }

    foreach (var question in questions.Take(2))
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"   • {question}");
    }

    Console.ResetColor();
    Console.WriteLine();
}

static async Task<bool> CollectionExists(string qdrantUrl, string collectionName)
{
    using var client = new HttpClient();

    try
    {
        var response = await client.GetAsync($"{qdrantUrl}/collections/{collectionName}");

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

            // Check if collection has any points
            var pointsCount = result.GetProperty("result").GetProperty("points_count").GetInt32();
            return pointsCount > 0;
        }

        return false;
    }
    catch
    {
        return false;
    }
}

static List<string> SplitIntoSentences(string text)
{
    // Simple sentence splitting - can be improved with more sophisticated logic
    var sentences = new List<string>();
    var current = "";

    for (int i = 0; i < text.Length; i++)
    {
        current += text[i];

        // Check for sentence endings
        if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
            i + 1 < text.Length &&
            char.IsWhiteSpace(text[i + 1]))
        {
            sentences.Add(current.Trim());
            current = "";
        }
    }

    // Add any remaining text
    if (!string.IsNullOrWhiteSpace(current))
    {
        sentences.Add(current.Trim());
    }

    return sentences.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
}

static List<string> SplitLongSentence(string sentence, int maxLength)
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
                // Single word is too long, just add it
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