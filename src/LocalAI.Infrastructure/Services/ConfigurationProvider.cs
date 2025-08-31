using Microsoft.Extensions.Configuration;
using LocalAI.Core.Interfaces;

namespace LocalAI.Infrastructure.Services
{
    public class ConfigurationProvider : LocalAI.Core.Interfaces.IConfigurationProvider
    {
        private readonly IConfiguration _configuration;

        public ConfigurationProvider(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public string GetEmbeddingBaseUrl()
        {
            var baseUrl = _configuration["EmbeddingService:BaseUrl"];
            return string.IsNullOrWhiteSpace(baseUrl)
                ? throw new InvalidOperationException("EmbeddingService:BaseUrl configuration is required")
                : baseUrl;
        }

        public string GetEmbeddingModel()
        {
            var model = _configuration["EmbeddingService:Model"];
            return string.IsNullOrWhiteSpace(model)
                ? throw new InvalidOperationException("EmbeddingService:Model configuration is required")
                : model;
        }

        public string GetRagBaseUrl()
        {
            var baseUrl = _configuration["RAGService:BaseUrl"];
            return string.IsNullOrWhiteSpace(baseUrl)
                ? throw new InvalidOperationException("RAGService:BaseUrl configuration is required")
                : baseUrl;
        }

        public string GetRagModel()
        {
            var model = _configuration["RAGService:Model"];
            return string.IsNullOrWhiteSpace(model)
                ? throw new InvalidOperationException("RAGService:Model configuration is required")
                : model;
        }
    }
}
