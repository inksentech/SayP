namespace SayP.Application.CommandSchemas;

/// <summary>
/// Command schema for creating an appointment
/// </summary>
public class CreateAppointmentCommand
{
    public string ServiceName { get; set; } = string.Empty;
    public Guid? ServiceId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerPhone { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }

    public static readonly string JsonSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""serviceName"": {
      ""type"": ""string"",
      ""description"": ""Name of the service (e.g., 'Saç Kesimi', 'Boyama')""
    },
    ""serviceId"": {
      ""type"": ""string"",
      ""description"": ""Optional: Service/Product ID if known""
    },
    ""customerName"": {
      ""type"": ""string"",
      ""description"": ""Customer name""
    },
    ""customerId"": {
      ""type"": ""string"",
      ""description"": ""Optional: Customer ID if known""
    },
    ""customerPhone"": {
      ""type"": ""string"",
      ""description"": ""Customer phone number""
    },
    ""appointmentDate"": {
      ""type"": ""string"",
      ""format"": ""date"",
      ""description"": ""Appointment date (YYYY-MM-DD)""
    },
    ""startTime"": {
      ""type"": ""string"",
      ""format"": ""time"",
      ""description"": ""Start time (HH:mm)""
    },
    ""durationMinutes"": {
      ""type"": ""integer"",
      ""description"": ""Duration in minutes""
    },
    ""notes"": {
      ""type"": ""string"",
      ""description"": ""Additional notes""
    }
  },
  ""required"": [""serviceName"", ""appointmentDate""]
}";
}
