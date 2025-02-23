namespace Nexi.Data.Models
{
    public class AIResponse
    {
        public string Text { get; set; } = string.Empty;
        public float[] Embeddings { get; set; } = Array.Empty<float>();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
