using System.Text.Json;

var httpClient = new HttpClient();
var baseUrl = "http://localhost:6333";

try
{
    Console.WriteLine("Testing Qdrant connection...");

    // Test connection with REST API
    var response = await httpClient.GetAsync($"{baseUrl}/collections");
    var content = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"✅ Connected to Qdrant successfully!");
    Console.WriteLine($"📊 Collections response: {content}");

    // Create a test collection via REST API
    var collectionName = "test-knowledge";
    Console.WriteLine($"\n🔧 Creating test collection '{collectionName}'...");

    var createPayload = JsonSerializer.Serialize(new
    {
        vectors = new
        {
            size = 384,
            distance = "Cosine"
        }
    });

    var createContent = new StringContent(createPayload, System.Text.Encoding.UTF8, "application/json");
    var createResponse = await httpClient.PutAsync($"{baseUrl}/collections/{collectionName}", createContent);

    if (createResponse.IsSuccessStatusCode)
    {
        Console.WriteLine("✅ Test collection created!");
    }
    else
    {
        var errorContent = await createResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"⚠️ Collection creation response: {errorContent}");
    }

    // List collections again
    response = await httpClient.GetAsync($"{baseUrl}/collections");
    content = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"📊 Final collections: {content}");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();