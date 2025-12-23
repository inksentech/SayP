namespace SayP.Application.CommandSchemas;

/// <summary>
/// Command schema for creating a product
/// AI will extract this structure from natural language
/// </summary>
public class CreateProductCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Unit { get; set; }
    public decimal? TaxRate { get; set; }
    public string? Category { get; set; }
    public string? Barcode { get; set; }

    /// <summary>
    /// JSON Schema for AI prompt
    /// </summary>
    public static string JsonSchema => @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"": {
      ""type"": ""string"",
      ""description"": ""Product name""
    },
    ""description"": {
      ""type"": ""string"",
      ""description"": ""Product description (optional)""
    },
    ""price"": {
      ""type"": ""number"",
      ""description"": ""Product price""
    },
    ""unit"": {
      ""type"": ""string"",
      ""description"": ""Unit of measurement (e.g., 'adet', 'kg', 'litre')""
    },
    ""taxRate"": {
      ""type"": ""number"",
      ""description"": ""Tax rate (e.g., 0.18 for 18% VAT)""
    },
    ""category"": {
      ""type"": ""string"",
      ""description"": ""Product category (optional)""
    },
    ""barcode"": {
      ""type"": ""string"",
      ""description"": ""Product barcode (optional)""
    }
  },
  ""required"": [""name"", ""price""]
}";
}
