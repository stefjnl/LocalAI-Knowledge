using LocalAI.Web.Components;
using LocalAI.Web.Services;
using LocalAI.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add configuration for API settings
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

// Configure data protection for Docker environment
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/data-protection-keys"))
    .SetApplicationName("LocalAI.Web");

// HTTP Client for API calls with correct configuration
builder.Services.AddHttpClient<IApiService, ApiService>(client =>
{
    // Your API service handles the base URL configuration internally
    client.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for document processing
});

// Register API service
builder.Services.AddScoped<IApiService, ApiService>();

// Register Conversation service
builder.Services.AddScoped<LocalAI.Web.Services.IConversationService, ConversationService>();

// Register Conversation Export service
builder.Services.AddScoped<IConversationExportService, ConversationExportService>();

// Register Chat API service
builder.Services.AddScoped<IChatApiService, ChatApiService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // Only use HSTS in production, not in Docker development
    if (!app.Environment.IsEnvironment("Docker"))
    {
        app.UseHsts();
    }
}

// Only use HTTPS redirection in production, not in Docker development
if (!app.Environment.IsEnvironment("Docker"))
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
