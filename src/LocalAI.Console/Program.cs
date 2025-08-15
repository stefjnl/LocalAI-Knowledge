using DotNetEnv;
using LocalAI.Core.Interfaces;
using LocalAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

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

// Create directories from configuration
var transcriptsPath = configuration["DocumentPaths:Transcripts"];
var pdfsPath = configuration["DocumentPaths:PDFs"];

Directory.CreateDirectory(transcriptsPath);
Directory.CreateDirectory(pdfsPath);
Directory.CreateDirectory(Path.Combine(pdfsPath, "llms"));

// Helper methods for enhanced logging
void LogProgress(string message)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    Console.ResetColor();
}

void LogSuccess(string message)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ {message}");
    Console.ResetColor();
}

void LogWarning(string message)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ {message}");
    Console.ResetColor();
}

void LogError(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ {message}");
    Console.ResetColor();
}

void LogFileCount(string fileType, int count)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📁 Found {count} {fileType} files");
    Console.ResetColor();
}

try
{
    var displayService = serviceProvider.GetRequiredService<IDisplayService>();
    var vectorSearchService = serviceProvider.GetRequiredService<IVectorSearchService>();
    var documentProcessor = serviceProvider.GetRequiredService<IDocumentProcessor>();
    var ragService = serviceProvider.GetRequiredService<IRAGService>();

    LogProgress("🚀 Starting LocalAI Knowledge Processing Pipeline");
    LogProgress("================================================");

    // Check available files before processing (from configuration)
    // Debug logging
    LogProgress($"Transcripts path from config: '{transcriptsPath}'");
    LogProgress($"PDFs path from config: '{pdfsPath}'");
    LogProgress($"Current directory: {Directory.GetCurrentDirectory()}");

    if (string.IsNullOrEmpty(transcriptsPath) || string.IsNullOrEmpty(pdfsPath))
    {
        LogError("DocumentPaths configuration is missing or empty!");
        return;
    }

    var transcriptFiles = Directory.GetFiles(transcriptsPath, "*.txt", SearchOption.AllDirectories);
    var pdfFiles = Directory.GetFiles(pdfsPath, "*.pdf", SearchOption.AllDirectories);

    LogFileCount("transcript (.txt)", transcriptFiles.Length);
    LogFileCount("PDF", pdfFiles.Length);

    if (transcriptFiles.Length == 0 && pdfFiles.Length == 0)
    {
        LogWarning("No files found to process in data/transcripts or data/pdfs directories");
        LogProgress("Please add some .txt or .pdf files and try again");
        return;
    }

    // Test Qdrant connection
    LogProgress("🔍 Testing Qdrant connection...");
    var stopwatch = Stopwatch.StartNew();

    try
    {
        var collectionExists = await vectorSearchService.CollectionExistsAsync("knowledge");
        stopwatch.Stop();
        LogSuccess($"Qdrant connection successful ({stopwatch.ElapsedMilliseconds}ms)");

        if (collectionExists)
        {
            LogSuccess("Knowledge base collection 'knowledge' already exists");
            LogProgress("Checking if processing is needed...");

            // You could add a check here for document count if needed
            LogProgress("✅ Knowledge base ready - skipping to search mode");
            LogProgress("🔍 Ready for interactive search!");
            LogProgress("Ask questions about your documents (or 'quit' to exit):");
        }
        else
        {
            LogProgress("📚 Knowledge base collection not found - starting fresh processing");
            LogProgress("This will process all documents and create embeddings...");

            var processingStopwatch = Stopwatch.StartNew();

            LogProgress("📄 Starting document processing...");
            var allChunks = await documentProcessor.ProcessAllDocumentsAsync();

            processingStopwatch.Stop();
            LogSuccess($"Document processing completed in {processingStopwatch.Elapsed.TotalSeconds:F1} seconds");

            if (allChunks.Count > 0)
            {
                LogProgress($"💾 Storing {allChunks.Count} document chunks in vector database...");
                var storageStopwatch = Stopwatch.StartNew();

                await vectorSearchService.StoreDocumentsAsync(allChunks);

                storageStopwatch.Stop();
                LogSuccess($"Successfully stored {allChunks.Count} chunks in {storageStopwatch.Elapsed.TotalSeconds:F1} seconds");
                LogSuccess($"Total processing time: {(processingStopwatch.Elapsed + storageStopwatch.Elapsed).TotalMinutes:F1} minutes");
            }
            else
            {
                LogError("No document chunks were generated during processing");
                LogWarning("Check if your files are readable and in supported formats");
                return;
            }

            LogSuccess("\n🎉 Knowledge base initialization complete!");
            LogProgress("🔍 Ready for interactive search!");
            LogProgress("Ask questions about your documents (or 'quit' to exit):");
        }
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        LogError($"Failed to connect to Qdrant: {ex.Message}");
        LogWarning("Make sure Qdrant is running on http://localhost:6333");
        return;
    }

    // Interactive search loop with enhanced logging
    int queryCount = 0;
    while (true)
    {
        Console.Write("\n> ");
        var userQuery = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userQuery) || userQuery.ToLower() == "quit")
        {
            LogProgress("👋 Goodbye! Knowledge base is ready for web UI access.");
            break;
        }

        queryCount++;
        LogProgress($"🔎 Processing query #{queryCount}: \"{userQuery}\"");

        try
        {
            var searchStopwatch = Stopwatch.StartNew();

            // Search for relevant documents
            var searchResults = await vectorSearchService.SearchAsync(userQuery, limit: 8);
            searchStopwatch.Stop();

            if (searchResults.Count == 0)
            {
                LogWarning($"No relevant documents found (search took {searchStopwatch.ElapsedMilliseconds}ms)");
                continue;
            }

            LogSuccess($"Found {searchResults.Count} relevant chunks ({searchStopwatch.ElapsedMilliseconds}ms)");
            LogProgress("🤖 Generating AI response...");

            var ragStopwatch = Stopwatch.StartNew();
            // Generate RAG response
            var ragResponse = await ragService.GenerateResponseAsync(userQuery, searchResults.Take(5).ToList());
            ragStopwatch.Stop();

            LogSuccess($"AI response generated ({ragStopwatch.ElapsedMilliseconds}ms)");
            LogProgress($"Total query time: {(searchStopwatch.Elapsed + ragStopwatch.Elapsed).TotalSeconds:F1} seconds");

            // Display the enhanced response
            displayService.DisplayRAGResponse(userQuery, ragResponse, searchResults);
        }
        catch (Exception ex)
        {
            LogError($"Search error: {ex.Message}");
            LogWarning("This might be a temporary issue - try again or check your LM Studio connection");
        }
    }
}
catch (Exception ex)
{
    LogError($"Application error: {ex.Message}");
    Console.WriteLine($"\nFull error details:\n{ex}");
}
finally
{
    serviceProvider.Dispose();
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();