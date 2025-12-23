using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Enums;

namespace SayP.Application.Services;

/// <summary>
/// Validates slots against backend data
/// </summary>
public class BackendSlotValidator
{
    private readonly IBackendUserService _backendService;
    private readonly ILogger<BackendSlotValidator> _logger;

    public BackendSlotValidator(
        IBackendUserService backendService,
        ILogger<BackendSlotValidator> logger)
    {
        _backendService = backendService;
        _logger = logger;
    }

    /// <summary>
    /// Validate slots with backend data
    /// </summary>
    public async Task<BackendSlotValidationResult> ValidateSlotsAsync(
        CommandType commandType,
        Dictionary<string, object> slots,
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var result = new BackendSlotValidationResult
        {
            IsValid = true,
            Errors = new List<string>(),
            Suggestions = new List<string>(),
            EnrichedSlots = new Dictionary<string, object>(slots)
        };

        try
        {
            switch (commandType)
            {
                case CommandType.UpdateProduct:
                case CommandType.DeleteProduct:
                case CommandType.GetProduct:
                    await ValidateProductSlotsAsync(slots, tenantId, companyId, result, cancellationToken);
                    break;

                case CommandType.UpdateCustomer:
                case CommandType.DeleteCustomer:
                case CommandType.GetCustomer:
                    await ValidateCustomerSlotsAsync(slots, tenantId, companyId, result, cancellationToken);
                    break;

                case CommandType.CreateInvoice:
                    await ValidateInvoiceSlotsAsync(slots, tenantId, companyId, result, cancellationToken);
                    break;

                case CommandType.CreateProduct:
                    await ValidateNewProductSlotsAsync(slots, tenantId, companyId, result, cancellationToken);
                    break;

                case CommandType.CreateCustomer:
                    await ValidateNewCustomerSlotsAsync(slots, tenantId, companyId, result, cancellationToken);
                    break;
            }

            result.IsValid = !result.Errors.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating slots for {CommandType}", commandType);
            result.Errors.Add("Doğrulama sırasında bir hata oluştu.");
            result.IsValid = false;
        }

        return result;
    }

    /// <summary>
    /// Validate product slots (for update/delete/get operations)
    /// </summary>
    private async Task ValidateProductSlotsAsync(
        Dictionary<string, object> slots,
        Guid tenantId,
        Guid? companyId,
        BackendSlotValidationResult result,
        CancellationToken cancellationToken)
    {
        // Check if product name or ID is provided
        if (slots.TryGetValue("name", out var nameObj))
        {
            var productName = nameObj?.ToString();
            if (!string.IsNullOrWhiteSpace(productName))
            {
                // Search for product by name
                var searchResult = await _backendService.SearchProductsAsync(
                    productName, 
                    tenantId, 
                    companyId, 
                    cancellationToken);

                if (searchResult == null || !searchResult.Any())
                {
                    result.Errors.Add($"'{productName}' adında bir ürün bulunamadı.");
                    
                    // Try to suggest similar products
                    var allProducts = await _backendService.GetAllProductsAsync(tenantId, companyId, cancellationToken);
                    var suggestions = FindSimilarNames(productName, allProducts?.Select(p => p.Name).ToList() ?? new List<string>());
                    
                    if (suggestions.Any())
                    {
                        result.Suggestions.Add("Şunlardan birini mi demek istediniz?");
                        result.Suggestions.AddRange(suggestions.Select(s => $"• {s}"));
                    }
                }
                else if (searchResult.Count() == 1)
                {
                    // Exact match found, enrich with product ID
                    var product = searchResult.First();
                    result.EnrichedSlots["id"] = product.Id;
                    result.EnrichedSlots["name"] = product.Name;
                    
                    _logger.LogInformation("Product found and enriched: {ProductName} (ID: {ProductId})", 
                        product.Name, product.Id);
                }
                else
                {
                    // Multiple matches found
                    result.Errors.Add($"'{productName}' için birden fazla ürün bulundu. Lütfen daha spesifik olun.");
                    result.Suggestions.Add("Bulunan ürünler:");
                    result.Suggestions.AddRange(searchResult.Select(p => $"• {p.Name} (Fiyat: {p.Price:C})"));
                }
            }
        }
        else if (slots.TryGetValue("id", out var idObj))
        {
            // Validate product ID
            if (Guid.TryParse(idObj?.ToString(), out var productId))
            {
                var product = await _backendService.GetProductByIdAsync(productId, tenantId, companyId, cancellationToken);
                
                if (product == null)
                {
                    result.Errors.Add($"ID: {productId} ile bir ürün bulunamadı.");
                }
                else
                {
                    // Enrich with product details
                    result.EnrichedSlots["name"] = product.Name;
                    result.EnrichedSlots["price"] = product.Price;
                }
            }
        }
    }

    /// <summary>
    /// Validate customer slots
    /// </summary>
    private async Task ValidateCustomerSlotsAsync(
        Dictionary<string, object> slots,
        Guid tenantId,
        Guid? companyId,
        BackendSlotValidationResult result,
        CancellationToken cancellationToken)
    {
        if (slots.TryGetValue("name", out var nameObj))
        {
            var customerName = nameObj?.ToString();
            if (!string.IsNullOrWhiteSpace(customerName))
            {
                var searchResult = await _backendService.SearchCustomersAsync(
                    customerName, 
                    tenantId, 
                    companyId, 
                    cancellationToken);

                if (searchResult == null || !searchResult.Any())
                {
                    result.Errors.Add($"'{customerName}' adında bir müşteri bulunamadı.");
                    
                    var allCustomers = await _backendService.GetAllCustomersAsync(tenantId, companyId, cancellationToken);
                    var suggestions = FindSimilarNames(customerName, allCustomers?.Select(c => c.Name).ToList() ?? new List<string>());
                    
                    if (suggestions.Any())
                    {
                        result.Suggestions.Add("Şunlardan birini mi demek istediniz?");
                        result.Suggestions.AddRange(suggestions.Select(s => $"• {s}"));
                    }
                }
                else if (searchResult.Count() == 1)
                {
                    var customer = searchResult.First();
                    result.EnrichedSlots["id"] = customer.Id;
                    result.EnrichedSlots["customerId"] = customer.Id;
                    result.EnrichedSlots["name"] = customer.Name;
                    
                    _logger.LogInformation("Customer found and enriched: {CustomerName} (ID: {CustomerId})", 
                        customer.Name, customer.Id);
                }
                else
                {
                    result.Errors.Add($"'{customerName}' için birden fazla müşteri bulundu.");
                    result.Suggestions.Add("Bulunan müşteriler:");
                    result.Suggestions.AddRange(searchResult.Select(c => $"• {c.Name} ({c.Phone})"));
                }
            }
        }
    }

    /// <summary>
    /// Validate invoice slots
    /// </summary>
    private async Task ValidateInvoiceSlotsAsync(
        Dictionary<string, object> slots,
        Guid tenantId,
        Guid? companyId,
        BackendSlotValidationResult result,
        CancellationToken cancellationToken)
    {
        // Validate customer exists
        if (slots.TryGetValue("customerId", out var customerIdObj))
        {
            if (Guid.TryParse(customerIdObj?.ToString(), out var customerId))
            {
                var customer = await _backendService.GetCustomerByIdAsync(customerId, tenantId, companyId, cancellationToken);
                
                if (customer == null)
                {
                    result.Errors.Add("Belirtilen müşteri bulunamadı.");
                }
            }
        }

        // Validate products in items
        if (slots.TryGetValue("items", out var itemsObj) && itemsObj is List<Dictionary<string, object>> items)
        {
            foreach (var item in items)
            {
                if (item.TryGetValue("productName", out var productNameObj))
                {
                    var productName = productNameObj?.ToString();
                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        var products = await _backendService.SearchProductsAsync(
                            productName, 
                            tenantId, 
                            companyId, 
                            cancellationToken);

                        if (products == null || !products.Any())
                        {
                            result.Errors.Add($"Fatura kalemi '{productName}' bulunamadı.");
                        }
                        else if (products.Count() == 1)
                        {
                            // Enrich with product details
                            var product = products.First();
                            item["productId"] = product.Id;
                            item["unitPrice"] = product.Price;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validate new product slots (check for duplicates)
    /// </summary>
    private async Task ValidateNewProductSlotsAsync(
        Dictionary<string, object> slots,
        Guid tenantId,
        Guid? companyId,
        BackendSlotValidationResult result,
        CancellationToken cancellationToken)
    {
        if (slots.TryGetValue("name", out var nameObj))
        {
            var productName = nameObj?.ToString();
            if (!string.IsNullOrWhiteSpace(productName))
            {
                var existingProducts = await _backendService.SearchProductsAsync(
                    productName, 
                    tenantId, 
                    companyId, 
                    cancellationToken);

                if (existingProducts != null && existingProducts.Any())
                {
                    var exactMatch = existingProducts.FirstOrDefault(p => 
                        p.Name.Equals(productName, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        result.Errors.Add($"'{productName}' adında bir ürün zaten mevcut.");
                        result.Suggestions.Add($"Mevcut ürün: {exactMatch.Name} - Fiyat: {exactMatch.Price:C}");
                        result.Suggestions.Add("Güncellemek ister misiniz?");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validate new customer slots (check for duplicates)
    /// </summary>
    private async Task ValidateNewCustomerSlotsAsync(
        Dictionary<string, object> slots,
        Guid tenantId,
        Guid? companyId,
        BackendSlotValidationResult result,
        CancellationToken cancellationToken)
    {
        // Check for duplicate phone number
        if (slots.TryGetValue("phone", out var phoneObj))
        {
            var phone = phoneObj?.ToString();
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var existingCustomer = await _backendService.GetCustomerByPhoneAsync(
                    phone, 
                    tenantId, 
                    companyId, 
                    cancellationToken);

                if (existingCustomer != null)
                {
                    result.Errors.Add($"Bu telefon numarası ({phone}) zaten kayıtlı.");
                    result.Suggestions.Add($"Mevcut müşteri: {existingCustomer.Name}");
                    result.Suggestions.Add("Güncellemek ister misiniz?");
                }
            }
        }
    }

    /// <summary>
    /// Find similar names using Levenshtein distance
    /// </summary>
    private List<string> FindSimilarNames(string input, List<string> candidates, int maxDistance = 3)
    {
        if (string.IsNullOrWhiteSpace(input) || !candidates.Any())
            return new List<string>();

        var similarities = candidates
            .Select(c => new
            {
                Name = c,
                Distance = ComputeLevenshteinDistance(input.ToLowerInvariant(), c.ToLowerInvariant())
            })
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Take(5)
            .Select(x => x.Name)
            .ToList();

        return similarities;
    }

    /// <summary>
    /// Compute Levenshtein distance
    /// </summary>
    private int ComputeLevenshteinDistance(string str1, string str2)
    {
        var matrix = new int[str1.Length + 1, str2.Length + 1];

        for (int i = 0; i <= str1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= str2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= str1.Length; i++)
        {
            for (int j = 1; j <= str2.Length; j++)
            {
                var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[str1.Length, str2.Length];
    }
}

public class BackendSlotValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public Dictionary<string, object> EnrichedSlots { get; set; } = new();
}
