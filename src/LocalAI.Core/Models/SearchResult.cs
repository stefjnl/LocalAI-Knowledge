namespace LocalAI.Core.Models
{
    public class SearchResult
    {
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public float Score { get; set; }
        public string Type { get; set; } = string.Empty;
        public string PageInfo { get; set; } = string.Empty;
    }
}
