using DotNetEnv;
using LocalAI.Core.Interfaces;
using LocalAI.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from project root
var rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
var envPath = Path.Combine(rootPath, ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}

// Add configuration from both local and root
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile(Path.Combine(rootPath, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HTTP Client
builder.Services.AddHttpClient();

// Your existing services (same as Console app)
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IVectorSearchService, VectorSearchService>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<IRAGService, RAGService>();
builder.Services.AddScoped<IDisplayService, DisplayService>();

// CORS for Blazor app (if running separately)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
    {
        policy.WithOrigins("https://localhost:7001", "http://localhost:5000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazor");

// Create directories (relative to project root)
var dataPath = Path.Combine(rootPath, "data");
Directory.CreateDirectory(Path.Combine(dataPath, "transcripts"));
Directory.CreateDirectory(Path.Combine(dataPath, "pdfs"));
Directory.CreateDirectory(Path.Combine(dataPath, "pdfs", "llms"));

// API Endpoints
app.MapGet("/api/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

app.MapGet("/api/collection/exists", async (IVectorSearchService vectorSearch) =>
{
    var exists = await vectorSearch.CollectionExistsAsync("knowledge");
    return new { CollectionExists = exists };
});

app.MapPost("/api/documents/process", async (IDocumentProcessor processor, IVectorSearchService vectorSearch) =>
{
    try
    {
        var chunks = await processor.ProcessAllDocumentsAsync();
        if (chunks.Count > 0)
        {
            await vectorSearch.StoreDocumentsAsync(chunks);
            return Results.Ok(new
            {
                Success = true,
                ChunksProcessed = chunks.Count,
                Message = $"Successfully processed {chunks.Count} document chunks"
            });
        }

        return Results.Ok(new
        {
            Success = false,
            ChunksProcessed = 0,
            Message = "No documents found to process"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error processing documents: {ex.Message}");
    }
});

app.MapPost("/api/search", async (SearchRequest request, IVectorSearchService vectorSearch, IRAGService ragService) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest("Query cannot be empty");
        }

        // Search for relevant documents
        var searchResults = await vectorSearch.SearchAsync(request.Query, request.Limit ?? 8);

        if (searchResults.Count == 0)
        {
            return Results.Ok(new SearchResponse
            {
                Query = request.Query,
                HasResults = false,
                RAGResponse = "No relevant documents found for your query.",
                Sources = new List<LocalAI.Core.Models.SearchResult>()
            });
        }

        // Generate RAG response
        var ragResponse = await ragService.GenerateResponseAsync(request.Query, searchResults.Take(5).ToList());

        return Results.Ok(new SearchResponse
        {
            Query = request.Query,
            HasResults = true,
            RAGResponse = ragResponse,
            Sources = searchResults
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Search error: {ex.Message}");
    }
});

app.Run();

// DTOs for API
public record SearchRequest(string Query, int? Limit = 8);

public record SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool HasResults { get; set; }
    public string RAGResponse { get; set; } = string.Empty;
    public List<LocalAI.Core.Models.SearchResult> Sources { get; set; } = new();
}