namespace LocalAI.Core.Models
{
    public class DocumentChunk
    {
        public string Text { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public string Source { get; set; } = string.Empty;
        public string Metadata { get; set; } = string.Empty;
    }
}
