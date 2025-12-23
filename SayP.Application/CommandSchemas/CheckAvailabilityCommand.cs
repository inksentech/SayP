namespace SayP.Application.CommandSchemas;

/// <summary>
/// Command schema for checking appointment availability
/// </summary>
public class CheckAvailabilityCommand
{
    public DateTime Date { get; set; }
    public TimeSpan? Time { get; set; }
    public string? ServiceName { get; set; }
    public Guid? ServiceId { get; set; }

    public static readonly string JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""date"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Date to check availability (YYYY-MM-DD)""
    },
    ""time"": {
      ""type"": ""string"",
      ""format"": ""time"",
      ""description"": ""Specific time to check (HH:mm). If not provided, returns all available slots for the day""
    },
    ""serviceName"": {
      ""type"": ""string"",
      ""description"": ""Service name to check availability for""
    },
    ""serviceId"": {
      ""type"": ""string"",
      ""description"": ""Optional: Service ID if known""
    }
  },
  ""required"": [""date""]
}";
}
