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

    // 1. Read transcript file
    var transcriptPath = "data/transcripts/ousterhout-philosophy.txt";
    if (!File.Exists(transcriptPath))
    {
        Console.WriteLine($"❌ Transcript file not found: {transcriptPath}");
        return;
    }

    var content = await File.ReadAllTextAsync(transcriptPath);
    Console.WriteLine($"📖 Read transcript: {content.Length} characters");

    // 2. Split into chunks (simple approach - 500 char chunks with 50 char overlap)
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

    // 5. Test search
    Console.WriteLine("\n🔍 Testing search...");
    var searchQuery = "What does Ousterhout say about AI and software design?";
    await SearchKnowledge(searchQuery, openAiApiKey, qdrantUrl);

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
    var start = 0;

    while (start < text.Length)
    {
        var end = Math.Min(start + chunkSize, text.Length);
        var chunk = text.Substring(start, end - start).Trim();

        if (!string.IsNullOrWhiteSpace(chunk))
        {
            chunks.Add(chunk);
        }

        start += chunkSize - overlap;
    }

    return chunks;
}

static async Task<float[]> GetEmbedding(string text, string apiKey)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    var payload = JsonSerializer.Serialize(new
    {
        input = text,
        model = "text-embedding-3-small"
    });

    var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
    var response = await client.PostAsync("https://openrouter.ai/api/v1/embeddings", content);
    var responseContent = await response.Content.ReadAsStringAsync();

    // Debug: Always print the response to see what we're getting
    Console.WriteLine($"\nOpenRouter Response Status: {response.StatusCode}");
    Console.WriteLine($"OpenRouter Response Content: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");

    if (!response.IsSuccessStatusCode)
    {
        throw new Exception($"OpenRouter API failed: {response.StatusCode} - {responseContent}");
    }

    // For now, just return mock data to test the pipeline
    var random = new Random(text.GetHashCode());
    return Enumerable.Range(0, 1536).Select(_ => (float)random.NextDouble()).ToArray();
}

static async Task StoreInQdrant(List<(string text, float[] embedding)> embeddings, string qdrantUrl)
{
    using var client = new HttpClient();

    // Create collection first
    var collectionPayload = JsonSerializer.Serialize(new
    {
        vectors = new { size = 1536, distance = "Cosine" } // text-embedding-3-small is 1536 dimensions
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
    var queryEmbedding = await GetEmbedding(query, apiKey);

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

    Console.WriteLine($"🎯 Search results for: '{query}'");
    Console.WriteLine(responseContent);
}