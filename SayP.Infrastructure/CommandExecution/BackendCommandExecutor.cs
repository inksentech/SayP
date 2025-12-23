using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;
using System.Net.Http.Headers;
using System.Text;

namespace SayP.Infrastructure.CommandExecution;

/// <summary>
/// DEPRECATED: Executes commands on the main backend API
/// 
/// ⚠️ This class is deprecated. Use GenericCommandExecutor instead.
/// This will be removed in v3.0.0
/// </summary>
[Obsolete("BackendCommandExecutor is deprecated. Use GenericCommandExecutor for dynamic API execution. This will be removed in v3.0.0", false)]
public class BackendCommandExecutor : ICommandExecutor
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackendCommandExecutor> _logger;
    private readonly string _backendApiUrl;
    private readonly string _backendApiKey;

    public BackendCommandExecutor(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<BackendCommandExecutor> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _backendApiUrl = configuration["Backend:ApiUrl"] 
            ?? Environment.GetEnvironmentVariable("BACKEND_API_URL") 
            ?? "http://localhost:5245";
        
        _backendApiKey = configuration["SayP:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("SAYP_API_KEY") 
            ?? "SayP-Secret-Key-2024"; // Same default as BackendUserService

        if (!string.IsNullOrEmpty(_backendApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-SayP-Api-Key", _backendApiKey);
            _logger.LogInformation("BackendCommandExecutor configured with API key: {ApiKey}", 
                _backendApiKey.Substring(0, Math.Min(10, _backendApiKey.Length)) + "...");
        }
        else
        {
            _logger.LogWarning("BackendCommandExecutor: No API key configured!");
        }
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandType commandType,
        string commandJson,
        Guid tenantId,
        Guid? companyId = null,
        Guid? userCompanyId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing command {CommandType} for tenant {TenantId}", commandType, tenantId);

            // Add tenant context headers
            _httpClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
            _httpClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
            
            // Log current headers
            _logger.LogInformation("Request headers - API Key: {HasApiKey}, Tenant: {TenantId}", 
                _httpClient.DefaultRequestHeaders.Contains("X-SayP-Api-Key"), tenantId);

            if (companyId.HasValue)
            {
                _httpClient.DefaultRequestHeaders.Remove("X-Company-Id");
                _httpClient.DefaultRequestHeaders.Add("X-Company-Id", companyId.Value.ToString());
            }
            
            // Backend requires X-UserCompany-Id header (UserCompany.Id, not Company.Id)
            if (userCompanyId.HasValue)
            {
                _httpClient.DefaultRequestHeaders.Remove("X-UserCompany-Id");
                _httpClient.DefaultRequestHeaders.Add("X-UserCompany-Id", userCompanyId.Value.ToString());
                _logger.LogInformation("Added X-UserCompany-Id header: {UserCompanyId}", userCompanyId);
            }

            var (endpoint, method) = GetEndpointForCommand(commandType);
            
            // Parse and transform command JSON to match backend API expectations
            var transformedJson = TransformCommandJson(commandType, commandJson);
            var content = new StringContent(transformedJson, Encoding.UTF8, "application/json");

            HttpResponseMessage response;

            switch (method)
            {
                case "POST":
                    response = await _httpClient.PostAsync($"{_backendApiUrl}{endpoint}", content, cancellationToken);
                    break;
                case "PUT":
                    response = await _httpClient.PutAsync($"{_backendApiUrl}{endpoint}", content, cancellationToken);
                    break;
                case "GET":
                    // For GET requests, convert JSON to query string
                    var queryString = BuildQueryString(transformedJson);
                    var fullUrl = string.IsNullOrEmpty(queryString) 
                        ? $"{_backendApiUrl}{endpoint}" 
                        : $"{_backendApiUrl}{endpoint}?{queryString}";
                    
                    _logger.LogInformation("GET Request - Transformed JSON: {Json}", transformedJson);
                    _logger.LogInformation("GET Request - Query String: {QueryString}", queryString);
                    _logger.LogInformation("GET Request - Full URL: {FullUrl}", fullUrl);
                    
                    response = await _httpClient.GetAsync(fullUrl, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"HTTP method {method} not supported");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Command execution failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new CommandExecutionResult
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    ErrorMessage = $"Backend API error: {response.StatusCode}"
                };
            }

            _logger.LogInformation("Command {CommandType} executed successfully", commandType);

            return new CommandExecutionResult
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                ResultJson = responseContent
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandType}", commandType);
            return new CommandExecutionResult
            {
                Success = false,
                StatusCode = 500,
                ErrorMessage = ex.Message
            };
        }
    }

    private (string endpoint, string method) GetEndpointForCommand(CommandType commandType)
    {
        return commandType switch
        {
            // Product Commands
            CommandType.CreateProduct => ("/api/products", "POST"),
            CommandType.UpdateProduct => ("/api/products/by-code", "PUT"),  // Use by-code endpoint
            CommandType.DeleteProduct => ("/api/products", "DELETE"),
            CommandType.GetProduct => ("/api/products", "GET"),
            CommandType.ListProducts => ("/api/products", "GET"),
            CommandType.SearchProducts => ("/api/products/search", "GET"),
            CommandType.GetProductByCode => ("/api/products/by-code", "GET"),
            CommandType.GetProductsByCodes => ("/api/products/by-codes", "POST"),
            CommandType.GetTodaysProducts => ("/api/products/today", "GET"),
            
            // Customer Commands
            CommandType.CreateCustomer => ("/api/customers", "POST"),
            CommandType.UpdateCustomer => ("/api/customers", "PUT"),
            CommandType.DeleteCustomer => ("/api/customers", "DELETE"),
            CommandType.GetCustomer => ("/api/customers", "GET"),
            CommandType.ListCustomers => ("/api/customers", "GET"),
            CommandType.SearchCustomers => ("/api/customers/search", "GET"),
            
            // Invoice Commands
            CommandType.CreateInvoice => ("/api/invoices", "POST"),
            CommandType.UpdateInvoice => ("/api/invoices", "PUT"),
            CommandType.DeleteInvoice => ("/api/invoices", "DELETE"),
            CommandType.GetInvoice => ("/api/invoices", "GET"),
            CommandType.ListInvoices => ("/api/invoices", "GET"),
            CommandType.GetInvoiceStatus => ("/api/invoices/status", "GET"),
            CommandType.SendInvoice => ("/api/invoices/send", "POST"),
            
            // Contract Commands
            CommandType.CreateContract => ("/api/contracts", "POST"),
            CommandType.UpdateContract => ("/api/contracts", "PUT"),
            CommandType.DeleteContract => ("/api/contracts", "DELETE"),
            CommandType.GetContract => ("/api/contracts", "GET"),
            CommandType.ListContracts => ("/api/contracts", "GET"),
            
            // Appointment Commands
            CommandType.CreateAppointment => ("/api/appointments", "POST"),
            CommandType.UpdateAppointment => ("/api/appointments", "PUT"),
            CommandType.CancelAppointment => ("/api/appointments/cancel", "POST"),
            CommandType.GetAppointment => ("/api/appointments", "GET"),
            CommandType.ListAppointments => ("/api/appointments", "GET"),
            CommandType.CheckAvailability => ("/api/appointments/check-availability", "POST"),
            CommandType.GetAvailableSlots => ("/api/appointments/available-slots", "GET"),
            CommandType.ListTodayAppointments => ("/api/appointments/today", "GET"),
            CommandType.ListAppointmentsByDateRange => ("/api/appointments/date-range", "GET"),
            CommandType.ListAppointmentsByTimeRange => ("/api/appointments/time-range", "GET"),
            CommandType.CancelAppointmentsByTimeRange => ("/api/appointments/cancel-range", "POST"),
            
            // Analytics & Reports
            CommandType.GetSalesReport => ("/api/reports/sales", "GET"),
            CommandType.GetCustomerReport => ("/api/reports/customers", "GET"),
            CommandType.GetProductReport => ("/api/reports/products", "GET"),
            CommandType.GetFinancialSummary => ("/api/reports/financial", "GET"),
            
            // Bulk Operations
            CommandType.BulkCreateProducts => ("/api/products/bulk", "POST"),
            CommandType.BulkUpdateProducts => ("/api/products/bulk", "PUT"),
            CommandType.BulkDeleteProducts => ("/api/products/bulk", "DELETE"),
            
            // Smart Operations
            CommandType.SuggestProducts => ("/api/smart/suggest-products", "POST"),
            CommandType.PriceOptimization => ("/api/smart/optimize-price", "POST"),
            CommandType.StockAlert => ("/api/smart/stock-alert", "GET"),
            
            _ => throw new NotSupportedException($"Command type {commandType} not supported")
        };
    }

    private string TransformCommandJson(CommandType commandType, string commandJson)
    {
        try
        {
            // Parse the AI response
            dynamic parsed = JsonConvert.DeserializeObject(commandJson)!;
            
            // Extract the actual command data from the "command" field if it exists
            dynamic commandData = parsed.command ?? parsed;

            // Transform based on command type
            return commandType switch
            {
                CommandType.CreateProduct => TransformCreateProductCommand(commandData),
                CommandType.UpdateProduct => TransformUpdateProductCommand(commandData),
                CommandType.GetProduct => TransformGetProductCommand(commandData),
                CommandType.DeleteProduct => TransformDeleteProductCommand(commandData),
                CommandType.GetProductByCode => TransformGetProductByCodeCommand(commandData),
                CommandType.GetProductsByCodes => TransformGetProductsByCodesCommand(commandData),
                CommandType.GetTodaysProducts => TransformGetTodaysProductsCommand(commandData),
                CommandType.ListProducts => TransformListProductsCommand(commandData),
                CommandType.CreateCustomer => TransformCreateCustomerCommand(commandData),
                CommandType.GetCustomer => TransformGetCustomerInfoCommand(commandData),
                CommandType.CreateInvoice => TransformCreateInvoiceCommand(commandData),
                CommandType.GetInvoiceStatus => TransformGetInvoiceStatusCommand(commandData),
                CommandType.CreateContract => TransformCreateContractCommand(commandData),
                CommandType.CreateAppointment => TransformCreateAppointmentCommand(commandData),
                CommandType.ListAppointments => TransformListAppointmentsCommand(commandData),
                CommandType.CheckAvailability => TransformCheckAvailabilityCommand(commandData),
                CommandType.CancelAppointment => TransformCancelAppointmentCommand(commandData),
                _ => JsonConvert.SerializeObject(commandData) // Default: pass through
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error transforming command JSON, using original: {Json}", commandJson);
            return commandJson;
        }
    }

    private string TransformCreateProductCommand(dynamic commandData)
    {
        // Get tax rate and normalize it (if it's > 1, assume it's percentage and divide by 100)
        decimal taxRate = (decimal?)(commandData.taxRate ?? commandData.TaxRate ?? commandData.vatRate ?? commandData.VatRate) ?? 18m;
        if (taxRate > 1)
        {
            taxRate = taxRate / 100m; // Convert percentage to decimal (e.g., 18 -> 0.18)
        }

        var product = new
        {
            Name = commandData.name?.ToString() ?? commandData.Name?.ToString(),
            Description = commandData.description?.ToString() ?? commandData.Description?.ToString(),
            Price = (decimal?)(commandData.price ?? commandData.Price) ?? 0m,
            Unit = commandData.unit?.ToString() ?? commandData.Unit?.ToString() ?? "Adet",
            TaxRate = taxRate,
            StockQuantity = (decimal?)(commandData.stockQuantity ?? commandData.StockQuantity) ?? 0m,
            IsActive = true
        };

        return JsonConvert.SerializeObject(product);
    }

    private string TransformUpdateProductCommand(dynamic commandData)
    {
        // Get tax rate and normalize it if provided
        decimal? taxRate = (decimal?)(commandData.taxRate ?? commandData.TaxRate ?? commandData.vatRate ?? commandData.VatRate);
        if (taxRate.HasValue && taxRate.Value > 1)
        {
            taxRate = taxRate.Value / 100m;
        }

        // Get product identifiers
        var productName = commandData.name?.ToString() ?? commandData.Name?.ToString();
        var productId = commandData.id?.ToString() ?? commandData.Id?.ToString() ?? commandData.productId?.ToString();
        var productCode = commandData.code?.ToString() ?? commandData.Code?.ToString() ?? commandData.productCode?.ToString();

        var product = new
        {
            Id = productId,
            Code = productCode,  // Add code field for by-code endpoint
            Name = productName,
            Description = commandData.description?.ToString() ?? commandData.Description?.ToString(),
            Price = (decimal?)(commandData.price ?? commandData.Price),
            Unit = commandData.unit?.ToString() ?? commandData.Unit?.ToString(),
            TaxRate = taxRate,
            StockQuantity = (decimal?)(commandData.stockQuantity ?? commandData.StockQuantity ?? commandData.stock),
            IsActive = (bool?)(commandData.isActive ?? commandData.IsActive)
        };

        return JsonConvert.SerializeObject(product);
    }

    private string TransformGetProductCommand(dynamic commandData)
    {
        var productName = commandData.name?.ToString() ?? commandData.Name?.ToString();
        var productId = commandData.id?.ToString() ?? commandData.Id?.ToString();

        var query = new
        {
            Id = productId,
            Name = productName
        };

        return JsonConvert.SerializeObject(query);
    }

    private string TransformDeleteProductCommand(dynamic commandData)
    {
        var productName = commandData.name?.ToString() ?? commandData.Name?.ToString();
        var productId = commandData.id?.ToString() ?? commandData.Id?.ToString();

        var query = new
        {
            Id = productId,
            Name = productName
        };

        return JsonConvert.SerializeObject(query);
    }

    private string TransformGetProductByCodeCommand(dynamic commandData)
    {
        var code = commandData.code?.ToString() ?? commandData.Code?.ToString();

        var query = new
        {
            Code = code
        };

        return JsonConvert.SerializeObject(query);
    }

    private string TransformGetProductsByCodesCommand(dynamic commandData)
    {
        var codes = new List<string>();
        
        // Handle different possible formats
        if (commandData.codes != null)
        {
            if (commandData.codes is string codesString)
            {
                // Split comma-separated codes
                codes = codesString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim()).ToList();
            }
            else if (commandData.codes is IEnumerable<object> codesList)
            {
                codes = codesList.Select(c => c.ToString()).ToList();
            }
        }

        return JsonConvert.SerializeObject(codes);
    }

    private string TransformGetTodaysProductsCommand(dynamic commandData)
    {
        // No parameters needed for today's products
        return JsonConvert.SerializeObject(new { });
    }

    private string TransformCreateCustomerCommand(dynamic commandData)
    {
        var customer = new
        {
            Name = commandData.name?.ToString() ?? commandData.Name?.ToString(),
            Email = commandData.email?.ToString() ?? commandData.Email?.ToString(),
            Phone = commandData.phone?.ToString() ?? commandData.Phone?.ToString(),
            TaxId = commandData.taxId?.ToString() ?? commandData.TaxId?.ToString(),
            TaxOffice = commandData.taxOffice?.ToString() ?? commandData.TaxOffice?.ToString(),
            Address = commandData.address?.ToString() ?? commandData.Address?.ToString(),
            City = commandData.city?.ToString() ?? commandData.City?.ToString(),
            Country = commandData.country?.ToString() ?? commandData.Country?.ToString(),
            Notes = commandData.notes?.ToString() ?? commandData.Notes?.ToString()
        };

        return JsonConvert.SerializeObject(customer);
    }

    private string TransformCreateInvoiceCommand(dynamic commandData)
    {
        // Parse invoice items if they exist
        var items = new List<object>();
        
        if (commandData.items != null)
        {
            foreach (var item in commandData.items)
            {
                decimal quantity = (decimal?)(item.quantity ?? item.Quantity) ?? 1m;
                decimal unitPrice = (decimal?)(item.unitPrice ?? item.UnitPrice ?? item.price ?? item.Price) ?? 0m;
                decimal taxRate = (decimal?)(item.taxRate ?? item.TaxRate ?? item.vatRate ?? item.VatRate) ?? 0.18m;
                
                if (taxRate > 1)
                {
                    taxRate = taxRate / 100m;
                }

                items.Add(new
                {
                    ProductId = item.productId?.ToString() ?? item.ProductId?.ToString(),
                    ProductName = item.productName?.ToString() ?? item.ProductName?.ToString() ?? item.name?.ToString() ?? item.Name?.ToString(),
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    TaxRate = taxRate,
                    Description = item.description?.ToString() ?? item.Description?.ToString()
                });
            }
        }

        var invoice = new
        {
            CustomerId = commandData.customerId?.ToString() ?? commandData.CustomerId?.ToString(),
            CustomerName = commandData.customerName?.ToString() ?? commandData.CustomerName?.ToString(),
            InvoiceDate = commandData.invoiceDate?.ToString() ?? commandData.InvoiceDate?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            DueDate = commandData.dueDate?.ToString() ?? commandData.DueDate?.ToString(),
            Notes = commandData.notes?.ToString() ?? commandData.Notes?.ToString(),
            Items = items
        };

        return JsonConvert.SerializeObject(invoice);
    }

    private string TransformListProductsCommand(dynamic commandData)
    {
        var query = new
        {
            SearchQuery = commandData.searchQuery?.ToString() ?? commandData.SearchQuery?.ToString() ?? commandData.query?.ToString(),
            Category = commandData.category?.ToString() ?? commandData.Category?.ToString(),
            MinPrice = (decimal?)(commandData.minPrice ?? commandData.MinPrice),
            MaxPrice = (decimal?)(commandData.maxPrice ?? commandData.MaxPrice),
            IsActive = (bool?)(commandData.isActive ?? commandData.IsActive),
            PageNumber = (int?)(commandData.pageNumber ?? commandData.PageNumber) ?? 1,
            PageSize = (int?)(commandData.pageSize ?? commandData.PageSize ?? commandData.limit ?? commandData.Limit) ?? 10
        };

        return JsonConvert.SerializeObject(query);
    }

    private string TransformGetCustomerInfoCommand(dynamic commandData)
    {
        var query = new
        {
            CustomerId = commandData.customerId?.ToString() ?? commandData.CustomerId?.ToString() ?? commandData.id?.ToString(),
            CustomerName = commandData.customerName?.ToString() ?? commandData.CustomerName?.ToString() ?? commandData.name?.ToString(),
            Phone = commandData.phone?.ToString() ?? commandData.Phone?.ToString(),
            Email = commandData.email?.ToString() ?? commandData.Email?.ToString()
        };

        return JsonConvert.SerializeObject(query);
    }

    private string TransformGetInvoiceStatusCommand(dynamic commandData)
    {
        var query = new
        {
            InvoiceId = commandData.invoiceId?.ToString() ?? commandData.InvoiceId?.ToString() ?? commandData.id?.ToString(),
            InvoiceNumber = commandData.invoiceNumber?.ToString() ?? commandData.InvoiceNumber?.ToString() ?? commandData.number?.ToString()
        };

        return JsonConvert.SerializeObject(query);
    }

    private string TransformCreateContractCommand(dynamic commandData)
    {
        var contract = new
        {
            CustomerId = commandData.customerId?.ToString() ?? commandData.CustomerId?.ToString(),
            CustomerName = commandData.customerName?.ToString() ?? commandData.CustomerName?.ToString(),
            Title = commandData.title?.ToString() ?? commandData.Title?.ToString(),
            Description = commandData.description?.ToString() ?? commandData.Description?.ToString(),
            StartDate = commandData.startDate?.ToString() ?? commandData.StartDate?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            EndDate = commandData.endDate?.ToString() ?? commandData.EndDate?.ToString(),
            Amount = (decimal?)(commandData.amount ?? commandData.Amount) ?? 0m,
            Terms = commandData.terms?.ToString() ?? commandData.Terms?.ToString(),
            Notes = commandData.notes?.ToString() ?? commandData.Notes?.ToString()
        };

        return JsonConvert.SerializeObject(contract);
    }

    private string TransformCreateAppointmentCommand(dynamic commandData)
    {
        // Parse CustomerId - try Guid first, if fails use string (will be resolved by backend)
        Guid? customerId = null;
        var customerIdStr = commandData.customerId?.ToString() ?? commandData.CustomerId?.ToString();
        if (!string.IsNullOrEmpty(customerIdStr))
        {
            if (Guid.TryParse(customerIdStr, out Guid parsedCustomerId))
            {
                customerId = parsedCustomerId;
            }
        }

        // Parse ProductId (Service) - try Guid first
        Guid? productId = null;
        var productIdStr = commandData.productId?.ToString() ?? commandData.ProductId?.ToString() 
            ?? commandData.serviceId?.ToString() ?? commandData.ServiceId?.ToString();
        if (!string.IsNullOrEmpty(productIdStr))
        {
            if (Guid.TryParse(productIdStr, out Guid parsedProductId))
            {
                productId = parsedProductId;
            }
        }

        // Parse AppointmentDate
        DateTime? appointmentDate = null;
        var dateStr = commandData.appointmentDate?.ToString() ?? commandData.AppointmentDate?.ToString() 
            ?? commandData.date?.ToString() ?? commandData.Date?.ToString();
        if (!string.IsNullOrEmpty(dateStr))
        {
            if (DateTime.TryParse(dateStr, out DateTime parsedDate))
            {
                appointmentDate = parsedDate;
            }
        }

        // Parse StartTime
        TimeSpan? startTime = null;
        var timeStr = commandData.startTime?.ToString() ?? commandData.StartTime?.ToString() 
            ?? commandData.time?.ToString() ?? commandData.Time?.ToString();
        if (!string.IsNullOrEmpty(timeStr))
        {
            if (TimeSpan.TryParse(timeStr, out TimeSpan parsedTime))
            {
                startTime = parsedTime;
            }
        }

        // Extract names for backend resolution
        var customerName = commandData.customerName?.ToString() ?? commandData.CustomerName?.ToString();
        var serviceName = commandData.serviceName?.ToString() ?? commandData.ServiceName?.ToString();
        var customerPhone = commandData.customerPhone?.ToString() ?? commandData.CustomerPhone?.ToString();

        _logger.LogInformation("Appointment data - ProductId: {ProductId}, CustomerId: {CustomerId}, CustomerName: {CustomerName}, ServiceName: {ServiceName}, Date: {Date}, Time: {Time}",
            (object?)productId, (object?)customerId, (object?)customerName, (object?)serviceName, (object?)appointmentDate, (object?)startTime);

        var appointment = new
        {
            ProductId = productId,
            CustomerId = customerId,
            AppointmentDate = appointmentDate,
            StartTime = startTime,
            DurationMinutes = (int?)(commandData.durationMinutes ?? commandData.DurationMinutes ?? 30),
            CustomerNotes = commandData.notes?.ToString() ?? commandData.Notes?.ToString() 
                ?? commandData.customerNotes?.ToString() ?? commandData.CustomerNotes?.ToString(),
            
            // Include names for backend to resolve if IDs are missing
            CustomerName = customerName,
            ServiceName = serviceName,
            CustomerPhone = customerPhone
        };

        var json = JsonConvert.SerializeObject(appointment, new JsonSerializerSettings 
        { 
            NullValueHandling = NullValueHandling.Ignore 
        });
        
        _logger.LogInformation("Sending appointment request: {Json}", json);
        return json;
    }

    private string TransformListAppointmentsCommand(dynamic commandData)
    {
        // Parse and format dates
        string? date = ParseAndFormatDate(commandData.date?.ToString() ?? commandData.Date?.ToString());
        string? startDate = ParseAndFormatDate(commandData.startDate?.ToString() ?? commandData.StartDate?.ToString());
        string? endDate = ParseAndFormatDate(commandData.endDate?.ToString() ?? commandData.EndDate?.ToString());
        
        // Parse and format times
        string? startTime = ParseAndFormatTime(commandData.startTime?.ToString() ?? commandData.StartTime?.ToString());
        string? endTime = ParseAndFormatTime(commandData.endTime?.ToString() ?? commandData.EndTime?.ToString());

        var query = new
        {
            Date = date,
            StartDate = startDate,
            EndDate = endDate,
            StartTime = startTime,
            EndTime = endTime,
            Status = commandData.status?.ToString() ?? commandData.Status?.ToString(),
            ServiceName = commandData.serviceName?.ToString() ?? commandData.ServiceName?.ToString(),
            CustomerName = commandData.customerName?.ToString() ?? commandData.CustomerName?.ToString()
        };

        return JsonConvert.SerializeObject(query, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    private string TransformCheckAvailabilityCommand(dynamic commandData)
    {
        string? date = ParseAndFormatDate(commandData.date?.ToString() ?? commandData.Date?.ToString());
        string? time = ParseAndFormatTime(commandData.time?.ToString() ?? commandData.Time?.ToString());

        var query = new
        {
            Date = date,
            Time = time,
            ServiceName = commandData.serviceName?.ToString() ?? commandData.ServiceName?.ToString(),
            ServiceId = commandData.serviceId?.ToString() ?? commandData.ServiceId?.ToString()
        };

        return JsonConvert.SerializeObject(query, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    private string TransformCancelAppointmentCommand(dynamic commandData)
    {
        string? date = ParseAndFormatDate(commandData.date?.ToString() ?? commandData.Date?.ToString());
        string? startTime = ParseAndFormatTime(commandData.startTime?.ToString() ?? commandData.StartTime?.ToString());
        string? endTime = ParseAndFormatTime(commandData.endTime?.ToString() ?? commandData.EndTime?.ToString());

        var query = new
        {
            AppointmentId = commandData.appointmentId?.ToString() ?? commandData.AppointmentId?.ToString(),
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            Reason = commandData.reason?.ToString() ?? commandData.Reason?.ToString()
        };

        return JsonConvert.SerializeObject(query, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    /// <summary>
    /// Convert JSON object to query string for GET requests
    /// </summary>
    private string BuildQueryString(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "{}")
            return string.Empty;

        try
        {
            var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (obj == null || obj.Count == 0)
                return string.Empty;

            var queryParams = obj
                .Where(kvp => kvp.Value != null && !string.IsNullOrEmpty(kvp.Value.ToString()))
                .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value.ToString())}");

            return string.Join("&", queryParams);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build query string from JSON: {Json}", json);
            return string.Empty;
        }
    }

    /// <summary>
    /// Parse and format date to YYYY-MM-DD (ISO 8601)
    /// </summary>
    private string? ParseAndFormatDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return null;

        if (DateTime.TryParse(dateStr, out DateTime parsedDate))
        {
            return parsedDate.ToString("yyyy-MM-dd");
        }

        return null;
    }

    /// <summary>
    /// Parse and format time to HH:mm
    /// </summary>
    private string? ParseAndFormatTime(string? timeStr)
    {
        if (string.IsNullOrEmpty(timeStr))
            return null;

        // Try parsing as TimeSpan first
        if (TimeSpan.TryParse(timeStr, out TimeSpan parsedTime))
        {
            return parsedTime.ToString(@"hh\:mm");
        }

        // Try parsing as DateTime (in case it's "14:30" format)
        if (DateTime.TryParse(timeStr, out DateTime parsedDateTime))
        {
            return parsedDateTime.ToString("HH:mm");
        }

        return null;
    }
}
