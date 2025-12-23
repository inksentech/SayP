using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;
using SayP.Application.CommandSchemas;

namespace SayP.Application.Services;

/// <summary>
/// DEPRECATED: Routes natural language messages to structured commands using AI
/// Provider-agnostic: works with OpenAI, Anthropic, Azure OpenAI, etc.
/// 
/// ⚠️ This class is deprecated. Use DynamicIntentMapper instead.
/// This will be removed in v3.0.0
/// </summary>
[Obsolete("AICommandRouter is deprecated. Use DynamicIntentMapper for generic AI-powered intent mapping. This will be removed in v3.0.0", false)]
public class AICommandRouter
{
    private readonly IAIProvider _aiProvider;
    private readonly ILogger<AICommandRouter> _logger;

    // System prompt for command extraction
    private const string SystemPrompt = @"You are an AI assistant that extracts structured commands from natural language messages.
Your task is to analyze user messages and determine if they contain actionable commands.

Available commands:
1. CreateProduct - Create a new product/service
2. UpdateProduct - Update an existing product
3. ListProducts - List/search products
4. CreateCustomer - Create a new customer
5. GetCustomerInfo - Get customer information
6. CreateInvoice - Create a new invoice
7. CreateContract - Create a new contract
8. GetInvoiceStatus - Get status of an invoice
9. CreateAppointment - Create a new appointment for a service
10. ListAppointments - List appointments (today, date range, time range)
11. CheckAvailability - Check if a specific date/time is available
12. CancelAppointment - Cancel an appointment or appointments in a time range

When you identify a command, respond with JSON in this format:
{
  ""commandType"": ""CreateProduct"",
  ""requiresConfirmation"": true,
  ""confidence"": 0.95,
  ""command"": { /* command-specific JSON matching the schema */ },
  ""confirmationMessage"": ""I understood you want to create a product named 'X' with price Y. Is this correct?""
}

If the message is not a command (just a question or greeting), respond with:
{
  ""commandType"": ""None"",
  ""response"": ""Your natural language response here""
}

Important rules:
- Always validate that required fields are present
- If information is missing, ask for it instead of making assumptions
- Use Turkish language for confirmationMessage and response
- Set requiresConfirmation to true for destructive operations (create, update, delete)
- Set confidence score (0-1) based on how certain you are about the command
";

    public AICommandRouter(
        IAIProvider aiProvider,
        ILogger<AICommandRouter> _logger)
    {
        _aiProvider = aiProvider;
        this._logger = _logger;
    }

    /// <summary>
    /// Route a natural language message to a structured command
    /// </summary>
    public async Task<CommandRoutingResult> RouteMessageAsync(
        string message,
        Guid? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Routing message: {Message}", message);

            // Build prompt with schemas
            var prompt = BuildPrompt(message, null);

            // Call AI provider
            var aiResult = await _aiProvider.ExtractCommandAsync(
                prompt,
                null,
                cancellationToken);

            if (!aiResult.Success)
            {
                _logger.LogWarning("AI extraction failed: {Error}", aiResult.ErrorMessage);
                return CommandRoutingResult.Failed(aiResult.ErrorMessage ?? "AI extraction failed");
            }

            // Parse AI response
            var routingResult = ParseAIResponse(aiResult);
            
            _logger.LogInformation("Command routed: Type={CommandType}, Confidence={Confidence}", 
                routingResult.CommandType, routingResult.Confidence);

            return routingResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing message");
            return CommandRoutingResult.Failed($"Error: {ex.Message}");
        }
    }

    private string BuildPrompt(string message, string? conversationContext)
    {
        var prompt = $"{SystemPrompt}\n\n";

        // Add command schemas
        prompt += "Command Schemas:\n\n";
        prompt += $"CreateProduct:\n{CreateProductCommand.JsonSchema}\n\n";
        prompt += $"CreateInvoice:\n{CreateInvoiceCommand.JsonSchema}\n\n";
        prompt += $"CreateAppointment:\n{CommandSchemas.CreateAppointmentCommand.JsonSchema}\n\n";
        prompt += $"ListAppointments:\n{CommandSchemas.ListAppointmentsCommand.JsonSchema}\n\n";
        prompt += $"CheckAvailability:\n{CommandSchemas.CheckAvailabilityCommand.JsonSchema}\n\n";
        prompt += $"CancelAppointment:\n{CommandSchemas.CancelAppointmentCommand.JsonSchema}\n\n";

        // Add conversation context if available
        if (!string.IsNullOrEmpty(conversationContext))
        {
            prompt += $"Previous conversation context:\n{conversationContext}\n\n";
        }

        prompt += $"User message: {message}\n\n";
        prompt += "Extract the command:";

        return prompt;
    }

    private CommandRoutingResult ParseAIResponse(AICommandResult aiResult)
    {
        try
        {
            var json = aiResult.CommandJson;
            if (string.IsNullOrEmpty(json))
            {
                return CommandRoutingResult.NoCommand("Mesajınızı anlayamadım. Lütfen daha açık bir şekilde belirtir misiniz?");
            }

            dynamic response = JsonConvert.DeserializeObject(json)!;
            string commandTypeStr = response.commandType?.ToString() ?? "None";

            // Check if it's a command or just a response
            if (commandTypeStr.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                string naturalResponse = response.response?.ToString() ?? "Nasıl yardımcı olabilirim?";
                _logger.LogInformation("Natural response: {Response}", naturalResponse);
                return CommandRoutingResult.NoCommand(naturalResponse);
            }

            // Parse command type
            if (!Enum.TryParse<CommandType>(commandTypeStr, out var commandType))
            {
                _logger.LogWarning("Unknown command type: {CommandType}", commandTypeStr);
                return CommandRoutingResult.NoCommand("Bu komutu anlayamadım.");
            }

            // Extract command data
            var commandData = response.command?.ToString() ?? "{}";
            var requiresConfirmation = response.requiresConfirmation?.ToString() == "True";
            double confidence = double.TryParse(response.confidence?.ToString(), out double conf) ? conf : 0.5;
            var confirmationMessage = response.confirmationMessage?.ToString();

            return new CommandRoutingResult
            {
                Success = true,
                IsCommand = true,
                CommandType = commandType,
                Parameters = commandData,
                CommandJson = commandData,
                RequiresConfirmation = requiresConfirmation,
                ConfirmationMessage = confirmationMessage,
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing AI response");
            return CommandRoutingResult.Failed($"Parse error: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of command routing
/// </summary>
public class CommandRoutingResult
{
    public bool Success { get; set; }
    public bool IsCommand { get; set; }
    public CommandType CommandType { get; set; }
    public string Parameters { get; set; } = string.Empty;
    public string? CommandJson { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? ConfirmationMessage { get; set; }
    public double Confidence { get; set; }
    public string? NaturalResponse { get; set; }
    public string? ErrorMessage { get; set; }

    public static CommandRoutingResult NoCommand(string response)
    {
        return new CommandRoutingResult
        {
            Success = true,
            IsCommand = false,
            NaturalResponse = response
        };
    }

    public static CommandRoutingResult Failed(string error)
    {
        return new CommandRoutingResult
        {
            Success = false,
            ErrorMessage = error
        };
    }
}
