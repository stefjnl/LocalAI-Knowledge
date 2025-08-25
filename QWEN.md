# Claude Code Configuration for .NET Core Applications

This file configures Qwen.md for .NET Core applications using Docker and Docker Compose.

## Project Structure

```
├── src/
│   ├── MyApp.API/
│   ├── MyApp.Core/
│   ├── MyApp.Infrastructure/
│   └── MyApp.Tests/
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
├── scripts/
├── .dockerignore
├── .gitignore
├── MyApp.sln
└── claude.md
```

## Development Environment

### Primary Stack
- **.NET Core/8.0**: Primary framework for API and business logic
- **Docker**: Containerization for consistent development and deployment
- **Docker Compose**: Multi-container application orchestration
- **Entity Framework Core**: ORM for database operations
- **SQL Server/PostgreSQL**: Primary database (containerized)
- **Redis**: Caching layer (containerized)

### Development Tools
- **Visual Studio/Rider**: Primary IDE
- **dotnet CLI**: Command-line interface for .NET operations
- **Entity Framework CLI**: Database migrations and scaffolding
- **xUnit**: Unit testing framework
- **Swagger/OpenAPI**: API documentation

## Docker Configuration

### Dockerfile Best Practices
- Use multi-stage builds for optimized image sizes
- Leverage .NET base images (`mcr.microsoft.com/dotnet/aspnet`, `mcr.microsoft.com/dotnet/sdk`)
- Copy only necessary files using `.dockerignore`
- Run applications as non-root user
- Use specific version tags for reproducible builds

### Docker Compose Services
Typical services include:
- **API service**: Main .NET Core application
- **Database service**: SQL Server or PostgreSQL
- **Cache service**: Redis
- **Message queue**: RabbitMQ or Azure Service Bus (if needed)

## Common Tasks and Commands

### Building and Running
```bash
# Build the solution
dotnet build

# Run the application
dotnet run --project src/MyApp.API

# Build Docker images
docker-compose build

# Start all services
docker-compose up -d

# View logs
docker-compose logs -f [service-name]
```

### Database Operations
```bash
# Add new migration
dotnet ef migrations add [MigrationName] --project src/MyApp.Infrastructure --startup-project src/MyApp.API

# Update database
dotnet ef database update --project src/MyApp.Infrastructure --startup-project src/MyApp.API

# Generate SQL script
dotnet ef migrations script --project src/MyApp.Infrastructure --startup-project src/MyApp.API
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test src/MyApp.Tests/
```

## Configuration Management

### Environment Variables
Common environment variables for containerized .NET applications:
- `ASPNETCORE_ENVIRONMENT`: Development, Staging, Production
- `ConnectionStrings__DefaultConnection`: Database connection string
- `Redis__ConnectionString`: Redis connection string
- `ASPNETCORE_URLS`: Binding URLs for the application

### appsettings.json Structure
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=db;Database=MyAppDb;User Id=sa;Password=YourPassword;TrustServerCertificate=true;"
  },
  "Redis": {
    "ConnectionString": "redis:6379"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Development Guidelines

### Code Organization
- Follow Clean Architecture or Onion Architecture patterns
- Separate concerns: API, Core/Domain, Infrastructure, Tests
- Use dependency injection for loose coupling
- Implement repository pattern for data access
- Use DTOs for API contracts

### Docker Best Practices
- Use `.dockerignore` to exclude unnecessary files
- Implement health checks for services
- Use volumes for persistent data (databases)
- Configure proper networking between services
- Use secrets management for sensitive data

### Testing Strategy
- Unit tests for business logic (Core layer)
- Integration tests for API endpoints
- Container tests for Docker configurations
- Use TestContainers for integration testing with real databases

## Troubleshooting

### Common Docker Issues
- **Port conflicts**: Ensure ports in docker-compose.yml don't conflict with local services
- **Volume mounting**: Check file permissions and paths for volume mounts
- **Network connectivity**: Verify service names match docker-compose service definitions
- **Environment variables**: Ensure proper variable substitution in docker-compose files

### .NET Core Specific Issues
- **Connection strings**: Verify database service names match docker-compose configuration
- **HTTPS in containers**: Configure HTTPS properly or disable for development
- **File watching**: Use polling file watcher in container environments
- **Timezone issues**: Set appropriate timezone in containers

## Performance Considerations

### Docker Optimization
- Use multi-stage builds to reduce image size
- Leverage Docker layer caching
- Minimize the number of layers in Dockerfile
- Use .dockerignore to reduce build context

### .NET Core Optimization
- Enable ReadyToRun images for faster startup
- Configure garbage collection appropriately
- Use connection pooling for database connections
- Implement proper caching strategies

## Security Considerations

- Never commit secrets to version control
- Use Docker secrets or environment variables for sensitive data
- Run containers with non-root users
- Keep base images updated
- Scan images for vulnerabilities
- Use HTTPS in production environments

## Development Workflow

1. **Local Development**: Use `dotnet run` for quick iterations
2. **Integration Testing**: Use `docker-compose up` to test full stack
3. **Database Changes**: Create migrations and test in containerized environment
4. **API Testing**: Use Swagger UI or Postman against containerized API
5. **Debugging**: Attach debugger to containerized applications when needed

## Useful Docker Commands

```bash
# Clean up Docker resources
docker system prune -a

# View container logs
docker logs -f <container-name>

# Execute commands in running container
docker exec -it <container-name> /bin/bash

# Copy files to/from container
docker cp <container-name>:/path/to/file ./local-path

# Inspect container or image
docker inspect <container-name-or-image>
```

## Additional Resources

- [.NET Core Docker Documentation](https://docs.microsoft.com/en-us/dotnet/core/docker/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [Entity Framework Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
- [ASP.NET Core Documentation](https://docs.microsoft.com/en-us/aspnet/core/)