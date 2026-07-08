namespace TarkovMonitor
{
    /// <summary>
    /// Represents a single tarkov.dev browser remote instance.
    /// </summary>
    public class BrowserRemote
    {
        /// <summary>
        /// The unique session ID for this remote (e.g., "abc123").
        /// </summary>
        public string Id { get; set; } = string.Empty;
    }
}
