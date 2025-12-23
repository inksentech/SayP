namespace SayP.Application.CommandSchemas;

/// <summary>
/// Command schema for listing appointments with various filters
/// </summary>
public class ListAppointmentsCommand
{
    public DateTime? Date { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Status { get; set; }
    public string? ServiceName { get; set; }
    public string? CustomerName { get; set; }

    public static readonly string JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""date"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Specific date to list appointments (YYYY-MM-DD). Use for 'bugün', 'yarın', etc.""
    },
    ""startDate"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Start date for date range queries""
    },
    ""endDate"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""End date for date range queries""
    },
    ""startTime"": {
      ""type"": ""string"",
      ""format"": ""time"",
      ""description"": ""Start time for time range queries (HH:mm)""
    },
    ""endTime"": {
      ""type"": ""string"",
      ""format"": ""time"",
      ""description"": ""End time for time range queries (HH:mm)""
    },
    ""status"": {
      ""type"": ""string"",
      ""enum"": [""Pending"", ""Confirmed"", ""Completed"", ""Cancelled""],
      ""description"": ""Filter by appointment status""
    },
    ""serviceName"": {
      ""type"": ""string"",
      ""description"": ""Filter by service name""
    },
    ""customerName"": {
      ""type"": ""string"",
      ""description"": ""Filter by customer name""
    }
  }
}";
}
