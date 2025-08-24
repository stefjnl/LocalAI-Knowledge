using DotNetEnv;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Services;
using LocalAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalAI.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
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

        // Configure JSON options to handle circular references
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });

        // HTTP Client
        builder.Services.AddHttpClient();

        // EF Core DbContext and stores
        builder.Services.AddDbContext<ChatSessionsDbContext>(options => options.UseSqlite("Data Source=localai.db"));
        builder.Services.AddScoped<IChatSessionStore, ChatSessionStore>();

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

        // Apply database migrations automatically in all environments
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ChatSessionsDbContext>();
            await dbContext.Database.MigrateAsync();
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

        app.MapGet("/api/documents/processed", (IDocumentProcessor processor) =>
        {
            try
            {
                var summary = ((DocumentProcessor)processor).GetProcessingSummary();
                return Results.Ok(new
                {
                    Success = true,
                    ProcessedFiles = summary.AllDocuments.Select(d => new
                    {
                        FileName = d.FileName,
                        DocumentType = d.DocumentType,
                        ChunksProcessed = d.ChunksProcessed,
                        ProcessingDuration = d.FormattedDuration,
                        ProcessedAt = d.FormattedProcessedAt,
                        Success = d.Success,
                        ErrorMessage = d.ErrorMessage
                    }).ToArray(),
                    TotalCount = summary.TotalDocuments,
                    TotalChunks = summary.TotalChunks,
                    SuccessfulDocuments = summary.SuccessfulDocuments,
                    FailedDocuments = summary.FailedDocuments
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving processed files: {ex.Message}");
            }
        });

        app.MapGet("/api/documents/last-run", (IDocumentProcessor processor) =>
        {
            try
            {
                var lastRunMetadata = ((DocumentProcessor)processor).GetLastRunMetadata();
                return Results.Ok(new
                {
                    Success = true,
                    DocumentsProcessed = lastRunMetadata.Count,
                    TotalChunks = lastRunMetadata.Sum(d => d.ChunksProcessed),
                    Documents = lastRunMetadata.Select(d => new
                    {
                        FileName = d.FileName,
                        DocumentType = d.DocumentType,
                        ChunksProcessed = d.ChunksProcessed,
                        ProcessingDuration = d.FormattedDuration,
                        ProcessedAt = d.FormattedProcessedAt,
                        Success = d.Success,
                        ErrorMessage = d.ErrorMessage
                    }).ToArray()
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving last run data: {ex.Message}");
            }
        });

        app.MapGet("/api/documents/summary", (IDocumentProcessor processor) =>
        {
            try
            {
                var summary = ((DocumentProcessor)processor).GetProcessingSummary();
                return Results.Ok(new
                {
                    Success = true,
                    TotalDocuments = summary.TotalDocuments,
                    TotalChunks = summary.TotalChunks,
                    SuccessfulDocuments = summary.SuccessfulDocuments,
                    FailedDocuments = summary.FailedDocuments,
                    LastRunDocuments = summary.LastRunDocuments,
                    LastRunChunks = summary.LastRunChunks,
                    LastRunDetails = summary.LastRunDetails.Select(d => new
                    {
                        FileName = d.FileName,
                        DocumentType = d.DocumentType,
                        ChunksProcessed = d.ChunksProcessed,
                        ProcessingDuration = d.FormattedDuration,
                        Success = d.Success
                    }).ToArray()
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving processing summary: {ex.Message}");
            }
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

        app.MapPost("/api/documents/upload", async (HttpRequest request, IDocumentProcessor processor, IVectorSearchService vectorSearch) =>
        {
            try
            {
                // Check if the request contains a file
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest("Invalid content type. Expected form data.");
                }

                var form = await request.ReadFormAsync();
                var file = form.Files.GetFile("file");

                if (file == null || file.Length == 0)
                {
                    return Results.BadRequest("No file uploaded.");
                }

                // Get document type from form
                var documentType = form["documentType"].ToString() ?? "transcript";

                // Save file to appropriate directory
                var fileName = Path.GetFileNameWithoutExtension(file.FileName);
                var fileExtension = Path.GetExtension(file.FileName);
                var safeFileName = $"{fileName}_{Guid.NewGuid()}{fileExtension}";

                var targetDirectory = documentType == "pdf" ? "data/pdfs/" : "data/transcripts/";
                var filePath = Path.Combine(targetDirectory, safeFileName);

                // Create directory if it doesn't exist
                Directory.CreateDirectory(targetDirectory);

                // Save file
                using var stream = File.Create(filePath);
                await file.CopyToAsync(stream);

                // Time the processing
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Process the uploaded file
                List<DocumentChunk> chunks = new();
                if (documentType == "pdf")
                {
                    chunks = await processor.ProcessPdfFileAsync(filePath);
                }
                else
                {
                    chunks = await processor.ProcessTextFileAsync(filePath);
                }

                stopwatch.Stop();

                if (chunks.Count > 0)
                {
                    await vectorSearch.StoreDocumentsAsync(chunks);

                    // Create processing metadata for tracking
                    var metadata = new ProcessingMetadata
                    {
                        FileName = Path.GetFileName(safeFileName),
                        DocumentType = documentType == "pdf" ? "PDF" : "Transcript",
                        ChunksProcessed = chunks.Count,
                        ProcessingDurationMs = stopwatch.ElapsedMilliseconds,
                        ProcessedAt = DateTime.UtcNow,
                        Success = true
                    };

                    // Save the metadata (cast to access enhanced methods)
                    ((DocumentProcessor)processor).SaveFileMetadata(metadata);

                    return Results.Ok(new
                    {
                        Success = true,
                        ChunksProcessed = chunks.Count,
                        ProcessingDuration = metadata.FormattedDuration,
                        Message = $"Successfully processed '{file.FileName}' ({chunks.Count} chunks in {metadata.FormattedDuration})"
                    });
                }

                return Results.Ok(new
                {
                    Success = false,
                    ChunksProcessed = 0,
                    ProcessingDuration = stopwatch.ElapsedMilliseconds < 1000 ? $"{stopwatch.ElapsedMilliseconds}ms" : $"{stopwatch.ElapsedMilliseconds / 1000.0:F1}s",
                    Message = $"No content found in '{file.FileName}'"
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error processing uploaded document: {ex.Message}");
            }
        });

        app.MapPost("/api/search", async (SearchRequest request, IVectorSearchService vectorSearch, IRAGService ragService, ILogger<Program> logger) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return Results.BadRequest("Query cannot be empty");
                }

                // Log incoming request details
                logger.LogInformation("[DEBUG] Search API received request: {Query}", request.Query);
                if (request.Context != null && request.Context.Any())
                {
                    logger.LogInformation("[DEBUG] Search API received conversation history: {Count} exchanges", request.Context.Count);
                    for (int i = 0; i < request.Context.Count; i++)
                    {
                        var exchange = request.Context[i];
                        logger.LogInformation("[DEBUG] Exchange {Index} - User: {Query}", i + 1, exchange.Query);
                        logger.LogInformation("[DEBUG] Exchange {Index} - Assistant: {Response}", i + 1, exchange.Response);
                    }
                }
                else
                {
                    logger.LogInformation("[DEBUG] Search API received no conversation history");
                }
                var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var searchStopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Search for relevant documents
                var searchResults = await vectorSearch.SearchAsync(request.Query, request.Limit ?? 8);
                searchStopwatch.Stop();
                var searchTime = searchStopwatch.ElapsedMilliseconds;

                if (searchResults.Count == 0)
                {
                    totalStopwatch.Stop();
                    var totalElapsedTime = totalStopwatch.ElapsedMilliseconds;

                    return Results.Ok(new SearchResponse
                    {
                        Query = request.Query,
                        HasResults = false,
                        RAGResponse = "No relevant documents found for your query.",
                        Sources = new List<LocalAI.Core.Models.SearchResult>(),
                        Timing = new TimingInfo
                        {
                            TotalTimeMs = totalElapsedTime,
                            SearchTimeMs = searchTime,
                            GenerationTimeMs = 0,
                            FormattedResponseTime = $"{totalElapsedTime / 1000.0:F2}s"
                        }
                    });
                }

                // Add context to the query if available
                var queryWithContext = request.Query;
                var context = request.Context ?? new List<ConversationExchange>();
                
                // Generate RAG response with timing, passing conversation context
                var generationStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var ragResponse = await ragService.GenerateResponseAsync(request.Query, searchResults.Take(5).ToList(), context);
                generationStopwatch.Stop();
                var generationTime = generationStopwatch.ElapsedMilliseconds;

                totalStopwatch.Stop();
                var totalTime = totalStopwatch.ElapsedMilliseconds;

                return Results.Ok(new SearchResponse
                {
                    Query = request.Query,
                    HasResults = true,
                    RAGResponse = ragResponse,
                    Sources = searchResults,
                    Timing = new TimingInfo
                    {
                        TotalTimeMs = totalTime,
                        SearchTimeMs = searchTime,
                        GenerationTimeMs = generationTime,
                        FormattedResponseTime = $"{totalTime / 1000.0:F2}s"
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Search error: {ex.Message}");
            }
        });

        // Chat Session API Endpoints
        app.MapGet("/api/sessions", async (IChatSessionStore sessionStore) =>
        {
            try
            {
                var sessions = await sessionStore.GetAllSessionsAsync();
                return Results.Ok(sessions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving sessions: {ex.Message}");
            }
        });

        app.MapPost("/api/sessions", async (IChatSessionStore sessionStore) =>
        {
            try
            {
                var session = await sessionStore.CreateSessionAsync("New Chat");
                return Results.Created($"/api/sessions/{session.SessionId}", session);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating session: {ex.Message}");
            }
        });

        app.MapGet("/api/sessions/{sessionId}/messages", async (Guid sessionId, IChatSessionStore sessionStore, int page = 1, int pageSize = 20) =>
        {
            try
            {
                var messages = await sessionStore.GetMessagesAsync(sessionId, page, pageSize);
                return Results.Ok(messages);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving messages: {ex.Message}");
            }
        });

        app.MapPost("/api/sessions/{sessionId}/messages", async (Guid sessionId, ChatMessageRequest request, IChatSessionStore sessionStore) =>
        {
            try
            {
                // Ensure session exists
                await sessionStore.EnsureSessionExistsAsync(sessionId);

                // Add message with specified role
                var message = await sessionStore.AddMessageAsync(sessionId, request.Role, request.Content);

                return Results.Created($"/api/sessions/{sessionId}/messages/{message.MessageId}", message);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error adding message: {ex.Message}");
            }
        });

        app.MapDelete("/api/sessions/{sessionId}", async (Guid sessionId, IChatSessionStore sessionStore) =>
        {
            try
            {
                await sessionStore.DeleteSessionAsync(sessionId);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting session: {ex.Message}");
            }
        });

        // Register enhanced search endpoints
        app.MapEnhancedSearchEndpoints();

        await app.RunAsync();
    }
}

// DTOs for API
public record SearchRequest(string Query, int? Limit = 8, List<LocalAI.Core.Models.ConversationExchange>? Context = null);

public record SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool HasResults { get; set; }
    public string RAGResponse { get; set; } = string.Empty;
    public List<LocalAI.Core.Models.SearchResult> Sources { get; set; } = new();
    public TimingInfo Timing { get; set; } = new();
}

public record TimingInfo
{
    public double TotalTimeMs { get; set; }
    public double SearchTimeMs { get; set; }
    public double GenerationTimeMs { get; set; }
    public string FormattedResponseTime { get; set; } = string.Empty;
}

public record ChatMessageRequest(string Content, MessageRole Role = MessageRole.User);
