using LocalAI.Web.Components;
using LocalAI.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add configuration for API settings
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// HTTP Client for API calls with correct configuration
builder.Services.AddHttpClient<IApiService, ApiService>(client =>
{
    // Your API service handles the base URL configuration internally
    client.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for document processing
});

// Register API service
builder.Services.AddScoped<IApiService, ApiService>();

// Register Conversation service
builder.Services.AddScoped<IConversationService, ConversationService>();

// Register Chat API service
builder.Services.AddScoped<IChatApiService, ChatApiService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
