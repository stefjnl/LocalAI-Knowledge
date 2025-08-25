# Use the official .NET 9 runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the .NET 9 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for restore
COPY ["src/LocalAI.Api/LocalAI.Api.csproj", "LocalAI.Api/"]
COPY ["src/LocalAI.Core/LocalAI.Core.csproj", "LocalAI.Core/"]
COPY ["src/LocalAI.Infrastructure/LocalAI.Infrastructure.csproj", "LocalAI.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "LocalAI.Api/LocalAI.Api.csproj"

# Copy all source code
COPY src/ .

# Build the application
RUN dotnet build "LocalAI.Api/LocalAI.Api.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "LocalAI.Api/LocalAI.Api.csproj" -c Release -o /app/publish

# Final stage - runtime image
FROM base AS final
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Copy environment file
COPY .env* ./

# Handle shutdown signals properly
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "LocalAI.Api.dll"]