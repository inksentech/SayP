namespace SayP.Application.CommandSchemas;

/// <summary>
/// Schema for listing products
/// </summary>
public class ListProductsCommand
{
    /// <summary>
    /// Search query (optional)
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Category filter (optional)
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Maximum number of results (default: 10)
    /// </summary>
    public int? Limit { get; set; } = 10;
}
