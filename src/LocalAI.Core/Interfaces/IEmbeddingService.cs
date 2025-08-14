namespace LocalAI.Core.Interfaces
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text, bool isQuery = false);
    }
}
