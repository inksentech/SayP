namespace SayP.Application.CommandSchemas;

/// <summary>
/// Schema for updating a product
/// </summary>
public class UpdateProductCommand
{
    /// <summary>
    /// Product ID or name to update
    /// </summary>
    public string ProductIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// New product name
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// New description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// New price
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// New unit (adet, kg, litre, etc.)
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// New tax rate (e.g., 0.18 for 18% VAT)
    /// </summary>
    public decimal? TaxRate { get; set; }

    /// <summary>
    /// New stock quantity
    /// </summary>
    public decimal? StockQuantity { get; set; }

    /// <summary>
    /// Is active?
    /// </summary>
    public bool? IsActive { get; set; }
}
