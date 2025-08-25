using DotNetEnv;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Services;
using LocalAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
            Console.WriteLine($"[INFO] Loaded environment variables from: {envPath}");
        }
        else
        {
            Console.WriteLine($"[WARN] .env file not found at: {envPath}");
        }

        // Add configuration from both local and root
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
            .AddJsonFile(Path.Combine(rootPath, "appsettings.json"), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
            
        // Debug: Print out the OpenRouter API key (masked) to verify it's loaded
        var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "NOT SET";
        if (openRouterKey != "NOT SET" && openRouterKey.Length > 10)
        {
            Console.WriteLine($"[INFO] OpenRouter API Key loaded: {openRouterKey.Substring(0, 5)}...{openRouterKey.Substring(openRouterKey.Length - 5)}");
        }
        else
        {
            Console.WriteLine($"[WARN] OpenRouter API Key: {openRouterKey}");
        }

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
        builder.Services.AddScoped<LocalAI.Core.Interfaces.IConversationService, LocalAI.Infrastructure.Services.FileBasedConversationService>();

        // Add logging
        builder.Services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        // Register the Code Assistant Service
        // If OpenRouter is configured, use Qwen3CoderService, otherwise use NullCodeAssistantService as fallback
        if (builder.Configuration.GetValue<bool>("OpenRouter:UseOpenRouter"))
        {
            builder.Services.AddScoped<ICodeAssistantService, Qwen3CoderService>();
        }
        else
        {
            builder.Services.AddScoped<ICodeAssistantService, NullCodeAssistantService>();
        }

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

        app.MapPost("/api/documents/process-new", async (IDocumentProcessor processor, IVectorSearchService vectorSearch) =>
        {
            try
            {
                var chunks = await processor.ProcessNewDocumentsAsync();
                if (chunks.Count > 0)
                {
                    await vectorSearch.StoreDocumentsAsync(chunks);
                    return Results.Ok(new
                    {
                        Success = true,
                        ChunksProcessed = chunks.Count,
                        Message = $"Successfully processed {chunks.Count} new document chunks"
                    });
                }

                return Results.Ok(new
                {
                    Success = false,
                    ChunksProcessed = 0,
                    Message = "No new documents found to process"
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error processing new documents: {ex.Message}");
            }
        });

        app.MapDelete("/api/documents/{documentName}", async (string documentName, IDocumentProcessor processor, IVectorSearchService vectorSearch) =>
        {
            try
            {
                // Delete document from vector database
                var vectorDeleteSuccess = await vectorSearch.DeleteDocumentAsync(documentName);
                
                if (vectorDeleteSuccess)
                {
                    // Delete metadata
                    processor.DeleteFileMetadata(documentName);
                    
                    return Results.Ok(new
                    {
                        Success = true,
                        Message = $"Successfully deleted document '{documentName}' and its embeddings"
                    });
                }
                else
                {
                    return Results.Problem($"Failed to delete document '{documentName}' from vector database");
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting document: {ex.Message}");
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

        // DEBUG ENDPOINTS
        app.MapGet("/api/debug/collection-stats", async (IVectorSearchService vectorSearch, ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("[DEBUG] Collection stats endpoint called");
                var collectionName = "knowledge"; // Default collection name
                var exists = await vectorSearch.CollectionExistsAsync(collectionName);
                
                if (!exists)
                {
                    return Results.Ok(new
                    {
                        Success = true,
                        CollectionExists = false,
                        Message = "Collection does not exist"
                    });
                }

                // Get detailed collection info
                using var httpClient = new HttpClient();
                var baseUrl = Environment.GetEnvironmentVariable("QDRANT_BASE_URL") ?? "http://host.docker.internal:6333";
                var response = await httpClient.GetAsync($"{baseUrl}/collections/{collectionName}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    return Results.Ok(new
                    {
                        Success = true,
                        CollectionExists = true,
                        CollectionInfo = result.GetProperty("result").ToString()
                    });
                }
                
                return Results.Problem($"Failed to get collection info: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEBUG] Error in collection stats endpoint");
                return Results.Problem($"Error retrieving collection stats: {ex.Message}");
            }
        });

        app.MapGet("/api/debug/documents", (IDocumentProcessor processor, ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("[DEBUG] Documents endpoint called");
                var metadata = ((DocumentProcessor)processor).GetAllProcessedDocumentsMetadata();
                
                return Results.Ok(new
                {
                    Success = true,
                    Documents = metadata.Select(m => new
                    {
                        FileName = m.FileName,
                        DocumentType = m.DocumentType,
                        ChunksProcessed = m.ChunksProcessed,
                        ProcessingDuration = m.FormattedDuration,
                        ProcessedAt = m.FormattedProcessedAt,
                        Success = m.Success,
                        ErrorMessage = m.ErrorMessage
                    }).ToArray()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEBUG] Error in documents endpoint");
                return Results.Problem($"Error retrieving documents: {ex.Message}");
            }
        });

        app.MapGet("/api/debug/search-chunks/{query}", async (string query, IVectorSearchService vectorSearch, ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("[DEBUG] Search chunks endpoint called with query: {Query}", query);
                var queryEmbedding = await ((VectorSearchService)vectorSearch).GenerateEmbeddingAsync(query, isQuery: true);

                // Use optimized search parameters for debugging
                var searchPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    query = queryEmbedding,
                    limit = 20, // Get more results for debugging
                    with_payload = true,
                    with_vector = false,
                    @params = new
                    {
                        hnsw_ef = 128,
                        exact = false,
                        indexed_only = true
                    }
                });

                using var httpClient = new HttpClient();
                var baseUrl = Environment.GetEnvironmentVariable("QDRANT_BASE_URL") ?? "http://host.docker.internal:6333";
                var collectionName = "knowledge";
                var searchContent = new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{baseUrl}/collections/{collectionName}/points/query", searchContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return Results.Problem($"Search error: {responseContent}");
                }

                var searchResult = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(responseContent);
                var results = searchResult.GetProperty("result").GetProperty("points").EnumerateArray().ToList();

                var searchResults = new List<object>();
                foreach (var result in results)
                {
                    var score = result.GetProperty("score").GetSingle();
                    var payload = result.GetProperty("payload");
                    var text = payload.GetProperty("text").GetString() ?? "";
                    var source = payload.GetProperty("source").GetString() ?? "";
                    var type = payload.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "transcript";
                    var pageInfo = payload.TryGetProperty("page_info", out var pageElement) ? pageElement.GetString() ?? "" : "";

                    searchResults.Add(new
                    {
                        Content = text,
                        Source = source,
                        Score = score,
                        Type = type,
                        PageInfo = pageInfo
                    });
                }

                return Results.Ok(new
                {
                    Success = true,
                    Query = query,
                    Results = searchResults
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEBUG] Error in search chunks endpoint");
                return Results.Problem($"Error in search chunks: {ex.Message}");
            }
        });

        app.MapGet("/api/debug/document-chunks/{filename}", async (string filename, IVectorSearchService vectorSearch, ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("[DEBUG] Document chunks endpoint called for filename: {Filename}", filename);
                
                // Search for chunks with specific source filename
                using var httpClient = new HttpClient();
                var baseUrl = Environment.GetEnvironmentVariable("QDRANT_BASE_URL") ?? "http://host.docker.internal:6333";
                var collectionName = "knowledge";
                
                var filterPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    filter = new
                    {
                        must = new[]
                        {
                            new
                            {
                                key = "source",
                                match = new
                                {
                                    value = filename
                                }
                            }
                        }
                    },
                    limit = 100, // Get up to 100 chunks
                    with_payload = true,
                    with_vector = false
                });

                var filterContent = new StringContent(filterPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{baseUrl}/collections/{collectionName}/points/scroll", filterContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return Results.Problem($"Document chunks error: {responseContent}");
                }

                var scrollResult = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(responseContent);
                var points = scrollResult.GetProperty("result").GetProperty("points").EnumerateArray().ToList();

                var chunks = new List<object>();
                foreach (var point in points)
                {
                    var id = point.GetProperty("id").GetInt32();
                    var payload = point.GetProperty("payload");
                    var text = payload.GetProperty("text").GetString() ?? "";
                    var source = payload.GetProperty("source").GetString() ?? "";
                    var type = payload.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "transcript";
                    var pageInfo = payload.TryGetProperty("page_info", out var pageElement) ? pageElement.GetString() ?? "" : "";

                    chunks.Add(new
                    {
                        Id = id,
                        Content = text,
                        Source = source,
                        Type = type,
                        PageInfo = pageInfo
                    });
                }

                return Results.Ok(new
                {
                    Success = true,
                    Filename = filename,
                    ChunkCount = chunks.Count,
                    Chunks = chunks
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEBUG] Error in document chunks endpoint");
                return Results.Problem($"Error retrieving document chunks: {ex.Message}");
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

        // Chat Conversation API Endpoints
        app.MapGet("/api/conversations", async (IConversationService conversationService) =>
        {
            try
            {
                var conversations = await conversationService.GetAllConversationsAsync();
                return Results.Ok(conversations);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving conversations: {ex.Message}");
            }
        });

        app.MapGet("/api/conversations/{conversationId}", async (Guid conversationId, IConversationService conversationService) =>
        {
            try
            {
                var conversation = await conversationService.GetConversationAsync(conversationId);
                if (conversation == null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(conversation);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving conversation: {ex.Message}");
            }
        });

        app.MapPost("/api/conversations", async (CreateConversationRequest request, IConversationService conversationService) =>
        {
            try
            {
                var conversation = await conversationService.CreateConversationAsync(request.Title);
                return Results.Created($"/api/conversations/{conversation.Id}", conversation);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating conversation: {ex.Message}");
            }
        });

        app.MapDelete("/api/conversations/{conversationId}", async (Guid conversationId, IConversationService conversationService) =>
        {
            try
            {
                var result = await conversationService.DeleteConversationAsync(conversationId);
                if (!result)
                {
                    return Results.NotFound();
                }
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting conversation: {ex.Message}");
            }
        });

        app.MapPost("/api/conversations/{conversationId}/messages", async (Guid conversationId, AddMessageRequest request, IConversationService conversationService) =>
        {
            try
            {
                var message = await conversationService.AddMessageAsync(conversationId, request.Role, request.Content);
                return Results.Created($"/api/conversations/{conversationId}/messages/{message.Id}", message);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error adding message to conversation: {ex.Message}");
            }
        });

        // Register enhanced search endpoints
        app.MapEnhancedSearchEndpoints();

        // Conditionally map the code assistant endpoint
        if (app.Configuration.GetValue<bool>("OpenRouter:UseOpenRouter"))
        {
            app.MapPost("/api/code", async (CodeRequest request, ICodeAssistantService codeAssistant, ILogger<Program> logger) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(request.Query))
                    {
                        return Results.BadRequest("Query cannot be empty");
                    }

                    var response = await codeAssistant.GenerateCodeResponseAsync(request.Query, request.Context ?? new List<ConversationExchange>());

                    return Results.Ok(new { Response = response });
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error generating code response: {ex.Message}");
                }
            });
        }

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

public record CreateConversationRequest(string Title = "New Chat");

public record AddMessageRequest(string Role, string Content);

public record CodeRequest(string Query, List<LocalAI.Core.Models.ConversationExchange>? Context = null);
