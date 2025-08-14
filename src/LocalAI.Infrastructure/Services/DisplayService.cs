using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Services
{
    public class DisplayService : IDisplayService
    {
        public void DisplaySearchResults(List<SearchResult> results, string query)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Found {results.Count} relevant passages for: \"{query}\"");
            Console.ResetColor();
            Console.WriteLine();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"📄 {result.Source}");

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" (Score: {result.Score:F2})");
                Console.ResetColor();

                var formattedContent = FormatContent(result.Content);
                Console.WriteLine($"\"{formattedContent}\"");

                if (i < results.Count - 1)
                {
                    Console.WriteLine();
                }
            }

            Console.WriteLine();
        }

        public void DisplayRAGResponse(string query, string response, List<SearchResult> sources)
        {
            Console.WriteLine($"🔍 Searching for: \"{query}\"");
            Console.WriteLine($"📚 Found {sources.Count} relevant sources");
            Console.WriteLine("🤖 Generating comprehensive response...\n");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("📝 Comprehensive Answer:");
            Console.ResetColor();
            Console.WriteLine(response);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("📖 Sources used:");
            for (int i = 0; i < Math.Min(5, sources.Count); i++)
            {
                var result = sources[i];
                Console.WriteLine($"   • {result.Source} (Score: {result.Score:F2})");
            }
            Console.ResetColor();

            Console.WriteLine();
            GenerateContextualFollowUpQuestions(query, sources);
        }

        public void DisplayProgress(string message)
        {
            Console.WriteLine(message);
        }

        public void DisplayError(string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ {error}");
            Console.ResetColor();
        }

        private static string FormatContent(string content)
        {
            return content
                .Replace("\n\n", " ")
                .Replace("\n", " ")
                .Trim()
                .Replace("  ", " ");
        }

        private static void GenerateContextualFollowUpQuestions(string originalQuery, List<SearchResult> searchResults)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("💡 Related questions you might want to explore:");
            Console.ResetColor();

            var questions = new List<string>();
            var sources = searchResults.Select(r => r.Source.ToLower()).ToList();
            var hasTranscript = sources.Any(s => s.Contains("transcript"));
            var hasPdf = sources.Any(s => s.Contains("pdf"));

            if (hasTranscript && hasPdf)
            {
                questions.Add("How do these software design principles apply to LLM development?");
                questions.Add("What are the connections between system complexity and AI model complexity?");
            }
            else if (hasPdf)
            {
                questions.Add("What are the practical implementation details for this concept?");
                questions.Add("How does this relate to other LLM techniques?");
            }
            else if (hasTranscript)
            {
                questions.Add("What examples does Ousterhout give for this concept?");
                questions.Add("How does this principle apply to different types of software systems?");
            }

            if (originalQuery.ToLower().Contains("tokeniz"))
            {
                questions.Add("How do different tokenization methods affect model performance?");
                questions.Add("What are the trade-offs between vocabulary size and efficiency?");
            }
            else if (originalQuery.ToLower().Contains("complex"))
            {
                questions.Add("What strategies help manage complexity in practice?");
                questions.Add("How do you measure and reduce complexity?");
            }

            foreach (var question in questions.Take(3))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"   • {question}");
            }

            Console.ResetColor();
            Console.WriteLine();
        }
    }
}