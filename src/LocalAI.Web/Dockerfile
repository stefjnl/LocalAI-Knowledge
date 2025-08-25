# Use the official .NET 9 runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the .NET 9 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for restore
COPY ["src/LocalAI.Web/LocalAI.Web.csproj", "LocalAI.Web/"]
COPY ["src/LocalAI.Core/LocalAI.Core.csproj", "LocalAI.Core/"]

# Restore dependencies
RUN dotnet restore "LocalAI.Web/LocalAI.Web.csproj"

# Copy all source code
COPY src/ .

# Build the application
RUN dotnet build "LocalAI.Web/LocalAI.Web.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "LocalAI.Web/LocalAI.Web.csproj" -c Release -o /app/publish

# Final stage - runtime image
FROM base AS final
WORKDIR /app

# Create directory for data protection keys
RUN mkdir -p /app/data-protection-keys

# Copy published application
COPY --from=publish /app/publish .

# Copy environment file
COPY .env* ./

# Handle shutdown signals properly
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "LocalAI.Web.dll"]