namespace SayP.Application.CommandSchemas;

/// <summary>
/// Command schema for creating an invoice
/// </summary>
public class CreateInvoiceCommand
{
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerTaxNo { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }
    public List<InvoiceItem> Items { get; set; } = new();
    public string? Notes { get; set; }

    public class InvoiceItem
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal? TaxRate { get; set; }
    }

    /// <summary>
    /// JSON Schema for AI prompt
    /// </summary>
    public static string JsonSchema => @"{
  ""type"": ""object"",
  ""properties"": {
    ""customerName"": {
      ""type"": ""string"",
      ""description"": ""Customer name or company name""
    },
    ""customerTaxNo"": {
      ""type"": ""string"",
      ""description"": ""Customer tax number (optional)""
    },
    ""date"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Invoice date (ISO format)""
    },
    ""dueDate"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Due date (optional, ISO format)""
    },
    ""items"": {
      ""type"": ""array"",
      ""description"": ""Invoice items"",
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""description"": {
            ""type"": ""string"",
            ""description"": ""Item description""
          },
          ""quantity"": {
            ""type"": ""number"",
            ""description"": ""Quantity""
          },
          ""unitPrice"": {
            ""type"": ""number"",
            ""description"": ""Unit price""
          },
          ""taxRate"": {
            ""type"": ""number"",
            ""description"": ""Tax rate (e.g., 0.18 for 18%)""
          }
        },
        ""required"": [""description"", ""quantity"", ""unitPrice""]
      }
    },
    ""notes"": {
      ""type"": ""string"",
      ""description"": ""Additional notes (optional)""
    }
  },
  ""required"": [""customerName"", ""items""]
}";
}
