using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LocalAI.Core.Interfaces;
using LocalAI.Infrastructure.Services;

// Load environment variables
Env.Load();

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Create service collection and configure DI
var services = new ServiceCollection();

// Configuration
services.AddSingleton<IConfiguration>(configuration);

// HTTP Client
services.AddHttpClient();

// Application Services
services.AddScoped<IEmbeddingService, EmbeddingService>();
services.AddScoped<IVectorSearchService, VectorSearchService>();
services.AddScoped<IDocumentProcessor, DocumentProcessor>();
services.AddScoped<IRAGService, RAGService>();
services.AddScoped<IDisplayService, DisplayService>();

// Build service provider
var serviceProvider = services.BuildServiceProvider();

// Create directories
Directory.CreateDirectory("data/transcripts");
Directory.CreateDirectory("data/pdfs");
Directory.CreateDirectory("data/pdfs/llms");

try
{
    var displayService = serviceProvider.GetRequiredService<IDisplayService>();
    var vectorSearchService = serviceProvider.GetRequiredService<IVectorSearchService>();
    var documentProcessor = serviceProvider.GetRequiredService<IDocumentProcessor>();
    var ragService = serviceProvider.GetRequiredService<IRAGService>();

    displayService.DisplayProgress("🚀 Starting knowledge processing pipeline...");

    // Check if collection already exists and has data
    if (await vectorSearchService.CollectionExistsAsync("knowledge"))
    {
        displayService.DisplayProgress("✅ Knowledge base already exists, skipping processing");
        displayService.DisplayProgress("🔍 Ready for interactive search!");
        displayService.DisplayProgress("Ask questions about your documents (or 'quit' to exit):");
    }
    else
    {
        displayService.DisplayProgress("📚 Processing all documents for the first time...");

        var allChunks = await documentProcessor.ProcessAllDocumentsAsync();

        if (allChunks.Count > 0)
        {
            displayService.DisplayProgress($"💾 Storing {allChunks.Count} total chunks in vector database...");
            await vectorSearchService.StoreDocumentsAsync(allChunks);
            displayService.DisplayProgress($"✅ Stored {allChunks.Count} documents successfully");
        }
        else
        {
            displayService.DisplayError("No documents found to process");
            return;
        }

        displayService.DisplayProgress("\n🔍 Ready for interactive search!");
        displayService.DisplayProgress("Ask questions about your documents (or 'quit' to exit):");
    }

    // Interactive search loop
    while (true)
    {
        Console.Write("\n> ");
        var userQuery = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userQuery) || userQuery.ToLower() == "quit")
            break;

        try
        {
            // Search for relevant documents
            var searchResults = await vectorSearchService.SearchAsync(userQuery, limit: 8);

            if (searchResults.Count == 0)
            {
                displayService.DisplayError("No relevant documents found for your query.");
                continue;
            }

            // Generate RAG response
            var ragResponse = await ragService.GenerateResponseAsync(userQuery, searchResults.Take(5).ToList());

            // Display the enhanced response
            displayService.DisplayRAGResponse(userQuery, ragResponse, searchResults);
        }
        catch (Exception ex)
        {
            displayService.DisplayError($"Search error: {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ Application error: {ex.Message}");
    Console.ResetColor();
}
finally
{
    serviceProvider.Dispose();
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();