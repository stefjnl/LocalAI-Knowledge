# Troubleshooting Guide

This document outlines the major issues encountered during LocalAI Knowledge development and their solutions.

## Docker Build Issues

### Problem: "failed to compute cache key" Errors

**Symptoms:**
```
failed to solve: failed to compute cache key: failed to calculate checksum of ref z21mjy0kjs2wmcwcekeetj08v::hxu4mzy779q9vxmcz5qd987zi: "/src": not found
```

**Root Cause:**
Docker build context was set to individual project folders, but Dockerfiles expected to copy from a `src/` structure.

**Original (Broken) Configuration:**
```yaml
localai-web:
  build:
    context: ./src/LocalAI.Web  # Wrong: build context too narrow
    dockerfile: Dockerfile
```

**Solution:**
```yaml
localai-web:
  build:
    context: .  # Fixed: Use root as context
    dockerfile: src/LocalAI.Web/Dockerfile  # Fixed: Full path to Dockerfile
```

**Why This Works:**
- Root context gives Docker access to entire project structure
- Dockerfiles can now find `src/LocalAI.Api/` paths correctly
- Full dockerfile path tells Docker exactly where to find build files

## Blazor Component Compilation Issues

### Problem: Character Literal Errors in Razor Files

**Symptoms:**
```
CS1012: Too many characters in character literal
CS1056: Unexpected character '\'
```

**Root Cause:**
Incorrect quote escaping in Razor `@onclick` handlers.

**Original (Broken) Code:**
```razor
@onclick="() => ExportResults(\"txt\")"
```

**Solution:**
```razor
@onclick='() => ExportResults("txt")'
```

**Alternative Solution:**
```razor
@onclick="() => ExportResults(&quot;txt&quot;)"
```

**Why This Works:**
- Single quotes for attribute wrapper avoid escaping conflicts
- Or use HTML entities for proper quote escaping

## Blazor Server Component Registration Issues

### Problem: SectionOutlet Duplicate Registration

**Symptoms:**
```
System.InvalidOperationException: There is already a subscriber to the content with the given section ID 'System.Object'.
```

**Root Cause:**
Multiple HTML documents with duplicate `<HeadOutlet>` components or conflicting render modes.

**Common Causes:**
1. **Duplicate HTML structure** in App.razor and MainLayout.razor
2. **Mixed @rendermode directives** across components
3. **Conflicting layout configurations**

**Solutions Attempted:**

#### Solution 1: Remove Duplicate HTML Structure
**Problem:** Both App.razor and MainLayout.razor had full HTML documents
```razor
<!-- App.razor: -->
<!DOCTYPE html><html><head><HeadOutlet /></head>...

<!-- MainLayout.razor: -->
<!DOCTYPE html><html><head><HeadOutlet /></head>...
```

**Fix:** MainLayout.razor should only contain layout structure:
```razor
@inherits LayoutComponentBase

<div class="page">
    <div class="sidebar">
        <NavMenu />
    </div>
    <main>
        <article class="content px-4">
            @Body
        </article>
    </main>
</div>
```

#### Solution 2: Remove Conflicting @rendermode Directives
**Problem:** Mixed render modes across components
```razor
@rendermode @(new InteractiveServerRenderMode())  <!-- Remove this -->
```

**Fix:** Let Program.cs handle render mode globally:
```csharp
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

#### Solution 3: Simplified Program.cs Configuration
**Problem:** Complex configuration causing registration conflicts

**Fix:** Minimal Program.cs:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<IApiService, ApiService>();
builder.Services.AddScoped<IApiService, ApiService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

## Component File Organization Issues

### Problem: Routing Conflicts with Pages/ Directory

**Symptoms:**
Components in `Components/Pages/` directory causing routing and registration issues.

**Solution:**
```powershell
# Move pages to correct location
Move-Item src/LocalAI.Web/Components/Pages/SearchPage.razor src/LocalAI.Web/Components/Search.razor
Move-Item src/LocalAI.Web/Components/Pages/Documents.razor src/LocalAI.Web/Components/Documents.razor

# Remove empty Pages directory
Remove-Item src/LocalAI.Web/Components/Pages -Recurse -Force
```

**Why This Works:**
- Blazor expects components in root `Components/` directory
- Pages/ subdirectory creates namespace conflicts

## Docker Container Management

### Problem: Container Name vs Service Name Confusion

**Symptoms:**
```
no such service: localai-knowledge-localai-web
```

**Solution:**
Use **service names** from docker-compose.yml, not image names:

```powershell
# Correct
docker compose restart localai-web

# Incorrect
docker compose restart localai-knowledge-localai-web
```

**Reference:**
- **Service name**: `localai-web` (defined in docker-compose.yml)
- **Container name**: `localai-web` (also defined in docker-compose.yml)
- **Image name**: `localai-knowledge-localai-web` (auto-generated by Docker)

### Force Container Cleanup

When containers are stuck or corrupted:

```powershell
# Target specific containers only
docker rm -f localai-web localai-api

# Remove specific images
docker rmi -f localai-knowledge-localai-web:latest localai-knowledge-localai-api:latest

# Clean rebuild
docker compose build --no-cache localai-web localai-api
docker compose up -d localai-web localai-api
```

## Outstanding Issues

### Unresolved: Blazor SectionOutlet Conflict

**Status:** Not fully resolved
**Impact:** Web UI fails to load with SectionOutlet registration errors
**Attempted Solutions:** Multiple Program.cs configurations, component restructuring, minimal setups
**Next Steps:** Consider switching to Blazor WebAssembly or using a fresh Blazor template

## Working Configuration Summary

### Docker Setup ✅
- **docker-compose.yml**: Root context with full dockerfile paths
- **Build process**: Successfully creates containers
- **API container**: Functional and accessible

### .NET Projects ✅
- **API project**: Builds and runs correctly
- **Core/Infrastructure**: No compilation issues
- **Console application**: Working vector search and RAG functionality

### Outstanding ❌
- **Web UI**: Blazor Server configuration conflicts
- **Component registration**: SectionOutlet duplicate subscriber errors

## Recovery Commands

If issues persist, revert to last working state:

```powershell
# Stash current changes
git stash push -m "UI enhancements - troubleshooting attempt"

# Revert to working commit
git reset --hard HEAD

# View stashed changes later
git stash list
git stash apply  # when ready to try again
```