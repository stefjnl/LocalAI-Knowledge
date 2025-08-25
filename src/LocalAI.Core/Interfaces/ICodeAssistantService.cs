
using LocalAI.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LocalAI.Core.Interfaces
{
    public interface ICodeAssistantService
    {
        Task<string> GenerateCodeResponseAsync(string query, List<ConversationExchange> conversationContext);
    }
}
