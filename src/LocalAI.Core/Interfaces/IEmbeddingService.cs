namespace LocalAI.Core.Interfaces
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false);
    }

    public interface IEmbeddingProvider
    {
        Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false);
        string GetProviderName();
    }

    public interface IConfigurationProvider
    {
        string GetEmbeddingBaseUrl();
        string GetEmbeddingModel();
        string GetRagBaseUrl();
        string GetRagModel();
    }
}
