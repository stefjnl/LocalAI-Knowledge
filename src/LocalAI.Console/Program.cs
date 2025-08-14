using System.Text.Json;
using OpenAI;
using DotNetEnv;

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

    // Check if collection already exists and has data
    if (await CollectionExists(qdrantUrl, "knowledge"))
    {
        Console.WriteLine("✅ Knowledge base already exists, skipping processing");
        Console.WriteLine("🔍 Ready for interactive search!");
        Console.WriteLine("Ask questions about the transcript (or 'quit' to exit):");
    }
    else
    {
        Console.WriteLine("📚 Processing documents for the first time...");

        // 1. Read transcript file
        var transcriptPath = "data/transcripts/ousterhout-philosophy.txt";
        if (!File.Exists(transcriptPath))
        {
            Console.WriteLine($"❌ Transcript file not found: {transcriptPath}");
            return;
        }

        var content = await File.ReadAllTextAsync(transcriptPath);
        Console.WriteLine($"📖 Read transcript: {content.Length} characters");

        // 2. Split into chunks (improved sentence-aware chunking)
        var chunks = SplitIntoChunks(content, 500, 50);
        Console.WriteLine($"✂️ Split into {chunks.Count} chunks");

        // 3. Generate embeddings for each chunk
        Console.WriteLine("🧠 Generating embeddings...");
        var embeddings = new List<(string text, float[] embedding)>();

        foreach (var chunk in chunks)
        {
            var embedding = await GetEmbedding(chunk, openAiApiKey);
            embeddings.Add((chunk, embedding));
            Console.Write(".");
        }
        Console.WriteLine($"\n✅ Generated {embeddings.Count} embeddings");

        // 4. Store in Qdrant
        Console.WriteLine("💾 Storing in Qdrant...");
        await StoreInQdrant(embeddings, qdrantUrl);

        Console.WriteLine("\n🔍 Ready for interactive search!");
        Console.WriteLine("Ask questions about the transcript (or 'quit' to exit):");
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

static async Task StoreInQdrant(List<(string text, float[] embedding)> embeddings, string qdrantUrl)
{
    using var client = new HttpClient();

    // Create collection first
    var collectionPayload = JsonSerializer.Serialize(new
    {
        vectors = new { size = 768, distance = "Cosine" } // NEW - Nomic embedding size
    });

    var createContent = new StringContent(collectionPayload, System.Text.Encoding.UTF8, "application/json");
    await client.PutAsync($"{qdrantUrl}/collections/knowledge", createContent);

    // Store points
    var points = embeddings.Select((item, index) => new
    {
        id = index,
        vector = item.embedding,
        payload = new { text = item.text, source = "ousterhout-philosophy" }
    }).ToArray();

    var pointsPayload = JsonSerializer.Serialize(new { points });
    var pointsContent = new StringContent(pointsPayload, System.Text.Encoding.UTF8, "application/json");

    var response = await client.PutAsync($"{qdrantUrl}/collections/knowledge/points", pointsContent);
    Console.WriteLine($"✅ Stored {points.Length} points in Qdrant");
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
        limit = 3,
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

            // Source header with score
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"📄 {GetSourceDisplayName(source)}");

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

static string GetSourceDisplayName(string source)
{
    // Handle different source types
    if (source.Contains("ousterhout"))
    {
        return "Ousterhout Philosophy Transcript";
    }

    // Future: handle PDFs and other sources
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