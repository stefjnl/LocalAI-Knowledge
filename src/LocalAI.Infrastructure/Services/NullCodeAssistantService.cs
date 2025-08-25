using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LocalAI.Infrastructure.Services
{
    /// <summary>
    /// A null implementation of ICodeAssistantService that can be used when no external service is configured.
    /// </summary>
    public class NullCodeAssistantService : ICodeAssistantService
    {
        public async Task<string> GenerateCodeResponseAsync(string query, List<ConversationExchange> conversationContext)
        {
            return await Task.FromResult("Code assistant service is not configured. Please check your appsettings.json for OpenRouter configuration.");
        }
    }
}