using LocalAI.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LocalAI.Core.Interfaces
{
    public interface ILlmProvider
    {
        Task<string> GenerateResponseAsync(string prompt);
        bool CanHandle(string providerType);
        string GetProviderName();
    }
}
