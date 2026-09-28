namespace GitPngTool.Models;

/// <summary>
/// Represents a single git commit displayed in the commits grid.
/// </summary>
public class CommitItem
{
    public string Hash { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
}
