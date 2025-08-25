using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace LocalAI.Api;

public static class EnhancedSearchEndpoint
{
    public static void MapEnhancedSearchEndpoints(this WebApplication app)
    {
        // Enhanced search endpoint with session integration
        app.MapPost("/api/search/conversational", async (
            [FromBody] ConversationalSearchRequest request,
            IVectorSearchService vectorSearch,
            IRAGService ragService,
            IChatSessionStore sessionStore) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return Results.BadRequest("Query cannot be empty");
                }

                // Start timing
                var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var searchStopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Get or create session
                var sessionId = request.SessionId ?? Guid.NewGuid();
                await sessionStore.EnsureSessionExistsAsync(sessionId);

                // Get conversation history for context
                var recentMessages = await sessionStore.GetRecentMessagesAsync(sessionId, 5);
                var conversationContext = recentMessages
                    .Select(m => new LocalAI.Core.Models.ConversationExchange
                    {
                        Query = m.Role == MessageRole.User ? m.Content : "",
                        Response = m.Role == MessageRole.Assistant ? m.Content : ""
                    })
                    .Where(ex => !string.IsNullOrEmpty(ex.Query) || !string.IsNullOrEmpty(ex.Response))
                    .ToList();

                // Search for relevant documents
                var searchResults = await vectorSearch.SearchAsync(request.Query, request.Limit ?? 8);
                searchStopwatch.Stop();
                var searchTime = searchStopwatch.ElapsedMilliseconds;

                if (searchResults.Count == 0)
                {
                    totalStopwatch.Stop();
                    var totalElapsedTime = totalStopwatch.ElapsedMilliseconds;

                    // Store user message even if no results
                    await sessionStore.AddMessageAsync(sessionId, MessageRole.User, request.Query);
                    await sessionStore.AddMessageAsync(sessionId, MessageRole.Assistant, "No relevant documents found for your query.");

                    return Results.Ok(new ConversationalSearchResponse
                    {
                        SessionId = sessionId,
                        Query = request.Query,
                        HasResults = false,
                        RAGResponse = "No relevant documents found for your query.",
                        Sources = new List<SearchResult>(),
                        Timing = new TimingInfo
                        {
                            TotalTimeMs = totalElapsedTime,
                            SearchTimeMs = searchTime,
                            GenerationTimeMs = 0,
                            FormattedResponseTime = $"{totalElapsedTime / 1000.0:F2}s"
                        }
                    });
                }

                // Build context from conversation history
                var queryWithContext = request.Query;
                if (conversationContext.Any())
                {
                    var contextText = string.Join("\n\n", conversationContext.Select(ex =>
                        $"User: {ex.Query}\nAssistant: {ex.Response}"));
                    queryWithContext = $"Conversation context:\n{contextText}\n\nCurrent question: {request.Query}";
                }

                // Generate RAG response with timing
                var generationStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var ragResponse = await ragService.GenerateResponseAsync(queryWithContext, searchResults.Take(5).ToList());
                generationStopwatch.Stop();
                var generationTime = generationStopwatch.ElapsedMilliseconds;

                totalStopwatch.Stop();
                var totalTime = totalStopwatch.ElapsedMilliseconds;

                // Store messages in session
                await sessionStore.AddMessageAsync(sessionId, MessageRole.User, request.Query);
                await sessionStore.AddMessageAsync(sessionId, MessageRole.Assistant, ragResponse);

                // Update session activity
                await sessionStore.UpdateSessionActivityAsync(sessionId);

                return Results.Ok(new ConversationalSearchResponse
                {
                    SessionId = sessionId,
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

        // Get conversation history for a session
        app.MapGet("/api/search/conversation/{sessionId}", async (Guid sessionId, IChatSessionStore sessionStore) =>
        {
            try
            {
                var messages = await sessionStore.GetMessagesAsync(sessionId, 1, 50);
                return Results.Ok(messages);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving conversation: {ex.Message}");
            }
        });

        // Clear conversation session
        app.MapDelete("/api/search/conversation/{sessionId}", async (Guid sessionId, IChatSessionStore sessionStore) =>
        {
            try
            {
                await sessionStore.DeleteSessionAsync(sessionId);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error clearing conversation: {ex.Message}");
            }
        });
    }
}

// DTOs for enhanced search
public record ConversationalSearchRequest(
    string Query,
    Guid? SessionId = null,
    int? Limit = 8);

public record ConversationalSearchResponse
{
    public Guid SessionId { get; set; }
    public string Query { get; set; } = string.Empty;
    public bool HasResults { get; set; }
    public string RAGResponse { get; set; } = string.Empty;
    public List<SearchResult> Sources { get; set; } = new();
    public TimingInfo Timing { get; set; } = new();
}
