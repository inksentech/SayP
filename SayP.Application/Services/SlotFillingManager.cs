using Microsoft.Extensions.Logging;
using SayP.Domain.Enums;
using SayP.Domain.Models;

namespace SayP.Application.Services;

/// <summary>
/// Manages slot filling for multi-turn conversations
/// </summary>
public class SlotFillingManager
{
    private readonly ILogger<SlotFillingManager> _logger;
    private readonly BackendSlotValidator? _backendValidator;

    public SlotFillingManager(
        ILogger<SlotFillingManager> logger,
        BackendSlotValidator? backendValidator = null)
    {
        _logger = logger;
        _backendValidator = backendValidator;
    }

    /// <summary>
    /// Get required slots for a command type
    /// </summary>
    public List<string> GetRequiredSlots(CommandType commandType)
    {
        return commandType switch
        {
            CommandType.CreateProduct => new() { "name", "price" },
            CommandType.UpdateProduct => new() { "name" }, // Accept name instead of id
            CommandType.DeleteProduct => new() { "name" }, // Accept name instead of id  
            CommandType.GetProduct => new() { "name" }, // Accept name instead of id
            CommandType.GetProductByCode => new() { "code" },
            CommandType.GetProductsByCodes => new() { "codes" },
            CommandType.GetTodaysProducts => new() { }, // No required slots
            CommandType.SearchProducts => new() { "searchQuery" },
            
            CommandType.CreateCustomer => new() { "name", "phone" },
            CommandType.UpdateCustomer => new() { "id" },
            CommandType.DeleteCustomer => new() { "id" },
            CommandType.GetCustomer => new() { "id" },
            
            CommandType.CreateInvoice => new() { "customerId", "items" },
            CommandType.UpdateInvoice => new() { "id" },
            CommandType.GetInvoice => new() { "id" },
            
            CommandType.CreateContract => new() { "customerId", "title", "amount" },
            
            // Appointment Commands
            CommandType.CreateAppointment => new() { "serviceName", "appointmentDate", "customerName" },
            CommandType.ListAppointments => new() { }, // No required - can list all
            CommandType.CheckAvailability => new() { "date" },
            CommandType.CancelAppointment => new() { }, // Either appointmentId or date+time range
            CommandType.ListTodayAppointments => new() { },
            CommandType.ListAppointmentsByDateRange => new() { "startDate", "endDate" },
            CommandType.ListAppointmentsByTimeRange => new() { "date", "startTime", "endTime" },
            CommandType.CancelAppointmentsByTimeRange => new() { "date", "startTime", "endTime" },
            
            _ => new()
        };
    }

    /// <summary>
    /// Get optional slots for a command type
    /// </summary>
    public List<string> GetOptionalSlots(CommandType commandType)
    {
        return commandType switch
        {
            CommandType.CreateProduct => new() { "description", "taxRate", "stockQuantity", "unit" },
            CommandType.UpdateProduct => new() { "price", "description", "taxRate", "stockQuantity" },
            CommandType.SearchProducts => new() { "minPrice", "maxPrice", "category", "pageSize" },
            
            CommandType.CreateCustomer => new() { "email", "address", "taxId", "taxOffice", "notes" },
            CommandType.UpdateCustomer => new() { "name", "phone", "email", "address" },
            
            CommandType.CreateInvoice => new() { "invoiceDate", "dueDate", "notes" },
            CommandType.CreateContract => new() { "startDate", "endDate", "terms", "notes" },
            
            // Appointment Commands
            CommandType.CreateAppointment => new() { "customerName", "customerPhone", "startTime", "durationMinutes", "notes" },
            CommandType.ListAppointments => new() { "date", "startDate", "endDate", "startTime", "endTime", "status", "serviceName" },
            CommandType.CheckAvailability => new() { "time", "serviceName" },
            CommandType.CancelAppointment => new() { "appointmentId", "date", "startTime", "endTime", "reason" },
            
            _ => new()
        };
    }

    /// <summary>
    /// Fill slots from extracted entities
    /// </summary>
    public SlotFillingResult FillSlots(
        CommandType commandType,
        Dictionary<string, object> extractedEntities,
        Dictionary<string, object>? existingSlots = null)
    {
        var requiredSlots = GetRequiredSlots(commandType);
        var optionalSlots = GetOptionalSlots(commandType);
        var allSlots = requiredSlots.Concat(optionalSlots).ToList();

        // Merge existing slots with new entities
        var filledSlots = new Dictionary<string, object>(existingSlots ?? new());
        
        foreach (var entity in extractedEntities)
        {
            var normalizedKey = NormalizeSlotName(entity.Key);
            if (allSlots.Contains(normalizedKey))
            {
                filledSlots[normalizedKey] = entity.Value;
            }
        }

        // Validate filled slots
        var validationErrors = ValidateSlots(commandType, filledSlots);

        // Find missing required slots
        var missingSlots = requiredSlots.Except(filledSlots.Keys).ToList();

        var result = new SlotFillingResult
        {
            IsComplete = !missingSlots.Any() && !validationErrors.Any(),
            FilledSlots = filledSlots,
            MissingSlots = missingSlots,
            ValidationErrors = validationErrors,
            Confidence = CalculateConfidence(requiredSlots.Count, filledSlots.Count, validationErrors.Count)
        };

        // Generate next question if slots are missing
        if (missingSlots.Any())
        {
            result.NextQuestion = GenerateSlotQuestion(commandType, missingSlots.First(), filledSlots);
        }

        _logger.LogInformation(
            "Slot filling for {CommandType}: {FilledCount}/{RequiredCount} required slots filled, Missing: {Missing}",
            commandType, filledSlots.Count, requiredSlots.Count, string.Join(", ", missingSlots));

        return result;
    }

    /// <summary>
    /// Fill slots with backend validation (async version)
    /// </summary>
    public async Task<SlotFillingResult> FillSlotsWithValidationAsync(
        CommandType commandType,
        Dictionary<string, object> extractedEntities,
        Guid tenantId,
        Guid? companyId,
        Dictionary<string, object>? existingSlots = null,
        CancellationToken cancellationToken = default)
    {
        // First do basic slot filling
        var result = FillSlots(commandType, extractedEntities, existingSlots);

        // If backend validator is available and slots are complete, validate with backend
        if (_backendValidator != null && result.IsComplete)
        {
            var backendValidation = await _backendValidator.ValidateSlotsAsync(
                commandType,
                result.FilledSlots,
                tenantId,
                companyId,
                cancellationToken);

            if (!backendValidation.IsValid)
            {
                result.IsComplete = false;
                result.ValidationErrors.AddRange(backendValidation.Errors);
                
                if (backendValidation.Suggestions.Any())
                {
                    result.NextQuestion = string.Join("\n", backendValidation.Suggestions);
                }
            }
            else
            {
                // Enrich slots with backend data
                result.FilledSlots = backendValidation.EnrichedSlots;
                _logger.LogInformation("Slots enriched with backend data");
            }
        }

        return result;
    }

    /// <summary>
    /// Normalize slot name (handle variations)
    /// </summary>
    private string NormalizeSlotName(string slotName)
    {
        var normalized = slotName.ToLower().Trim();
        
        // Handle common variations
        var mappings = new Dictionary<string, string>
        {
            { "productname", "name" },
            { "productid", "id" },
            // NOTE: customerName should NOT be normalized to "name" for appointments
            // { "customername", "name" },  // REMOVED - conflicts with appointment customerName
            { "customerid", "id" },
            { "phonenumber", "phone" },
            { "emailaddress", "email" },
            { "productprice", "price" },
            { "unitprice", "price" },
            { "vatrate", "taxRate" },
            { "kdv", "taxRate" },
            { "stock", "stockQuantity" },
            { "quantity", "stockQuantity" },
            { "search", "searchQuery" },
            { "query", "searchQuery" }
        };

        return mappings.ContainsKey(normalized) ? mappings[normalized] : slotName;
    }

    /// <summary>
    /// Validate filled slots
    /// </summary>
    private List<string> ValidateSlots(CommandType commandType, Dictionary<string, object> slots)
    {
        var errors = new List<string>();

        foreach (var slot in slots)
        {
            switch (slot.Key.ToLower())
            {
                case "price":
                    if (!IsValidPrice(slot.Value))
                        errors.Add("Fiyat geçerli bir sayı olmalıdır");
                    break;

                case "taxrate":
                    if (!IsValidTaxRate(slot.Value))
                        errors.Add("KDV oranı 0-100 arasında olmalıdır");
                    break;

                case "phone":
                    if (!IsValidPhone(slot.Value))
                        errors.Add("Telefon numarası geçerli değil");
                    break;

                case "email":
                    if (!IsValidEmail(slot.Value))
                        errors.Add("E-posta adresi geçerli değil");
                    break;

                case "stockquantity":
                    if (!IsValidQuantity(slot.Value))
                        errors.Add("Stok miktarı pozitif bir sayı olmalıdır");
                    break;
            }
        }

        return errors;
    }

    /// <summary>
    /// Generate question for missing slot
    /// </summary>
    private string GenerateSlotQuestion(
        CommandType commandType,
        string missingSlot,
        Dictionary<string, object> filledSlots)
    {
        var questions = new Dictionary<string, string>
        {
            // Product fields
            { "name", "Ürün/müşteri adını belirtir misiniz?" },
            { "price", "Fiyatı ne kadar olacak?" },
            { "id", "Hangi ürün/müşteri? ID veya adını söyleyebilir misiniz?" },
            { "stockQuantity", "Stok miktarı kaç olsun?" },
            { "taxRate", "KDV oranı nedir? (Varsayılan: %18)" },
            
            // Customer fields
            { "phone", "Telefon numarasını belirtir misiniz?" },
            { "email", "E-posta adresini belirtir misiniz?" },
            { "customerId", "Hangi müşteri için? Müşteri adını veya ID'sini söyleyebilir misiniz?" },
            
            // Appointment fields (user-friendly)
            { "appointmentDate", "📅 Randevu tarihi ne zaman olsun? (Örn: yarın, 5 Kasım, bugün)" },
            { "appointmentTime", "⏰ Saat kaçta olsun? (Örn: 14:00, 2 buçuk)" },
            { "serviceName", "💇 Hangi hizmet için? (Örn: Saç kesimi, Boyama)" },
            { "customerName", "👤 Müşteri adı nedir?" },
            { "duration", "⏱️ Randevu süresi ne kadar? (Örn: 30 dakika, 1 saat)" },
            
            // Invoice fields
            { "items", "Faturaya hangi ürünler eklensin?" },
            
            // Contract fields
            { "title", "Sözleşme başlığı ne olsun?" },
            { "amount", "Sözleşme tutarı ne kadar?" },
            
            // General
            { "searchQuery", "Ne aramak istiyorsunuz?" },
            { "description", "Açıklama eklemek ister misiniz?" }
        };

        if (questions.ContainsKey(missingSlot))
        {
            return questions[missingSlot];
        }

        return $"{missingSlot} bilgisini belirtir misiniz?";
    }

    /// <summary>
    /// Calculate confidence score
    /// </summary>
    private double CalculateConfidence(int requiredCount, int filledCount, int errorCount)
    {
        if (requiredCount == 0) return 1.0;
        
        var fillRatio = (double)filledCount / requiredCount;
        var errorPenalty = errorCount * 0.1;
        
        return Math.Max(0, Math.Min(1.0, fillRatio - errorPenalty));
    }

    // Validation helpers
    private bool IsValidPrice(object value)
    {
        if (value == null) return false;
        return decimal.TryParse(value.ToString(), out var price) && price >= 0;
    }

    private bool IsValidTaxRate(object value)
    {
        if (value == null) return false;
        if (!decimal.TryParse(value.ToString(), out var rate)) return false;
        
        // Handle both percentage (0-100) and decimal (0-1) formats
        return (rate >= 0 && rate <= 1) || (rate > 1 && rate <= 100);
    }

    private bool IsValidPhone(object value)
    {
        if (value == null) return false;
        var phone = value.ToString()?.Trim() ?? "";
        // Basic phone validation (can be improved)
        return phone.Length >= 10 && phone.All(c => char.IsDigit(c) || c == '+' || c == '-' || c == ' ' || c == '(' || c == ')');
    }

    private bool IsValidEmail(object value)
    {
        if (value == null) return false;
        var email = value.ToString()?.Trim() ?? "";
        return email.Contains("@") && email.Contains(".");
    }

    private bool IsValidQuantity(object value)
    {
        if (value == null) return false;
        return decimal.TryParse(value.ToString(), out var qty) && qty >= 0;
    }
}
