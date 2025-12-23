namespace SayP.Application.CommandSchemas;

/// <summary>
/// Command schema for cancelling appointments
/// </summary>
public class CancelAppointmentCommand
{
    public Guid? AppointmentId { get; set; }
    public DateTime? Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Reason { get; set; }

    public static readonly string JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""appointmentId"": {
      ""type"": ""string"",
      ""description"": ""Specific appointment ID to cancel""
    },
    ""date"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Date for bulk cancellation (YYYY-MM-DD)""
    },
    ""startTime"": {
      ""type"": ""string"",
      ""format"": ""time"",
      ""description"": ""Start time for time range cancellation (HH:mm)""
    },
    ""endTime"": {
      ""type"": ""string"",
      ""format"": ""time"",
      ""description"": ""End time for time range cancellation (HH:mm)""
    },
    ""reason"": {
      ""type"": ""string"",
      ""description"": ""Cancellation reason""
    }
  }
}";
}
