namespace SayP.Application.Interfaces;

/// <summary>
/// Service for communicating with the main backend to validate users and tenants
/// </summary>
public interface IBackendUserService
{
    /// <summary>
    /// Validate if a phone number is authorized and get tenant information
    /// </summary>
    /// <param name="phoneNumber">WhatsApp phone number</param>
    /// <returns>User authorization info if valid, null otherwise</returns>
    Task<UserAuthInfo?> ValidatePhoneNumberAsync(string phoneNumber);

    // Product operations
    Task<IEnumerable<ProductDto>?> SearchProductsAsync(string searchTerm, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductDto>?> GetAllProductsAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);
    Task<ProductDto?> GetProductByIdAsync(Guid productId, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);

    // Customer operations
    Task<IEnumerable<CustomerDto>?> SearchCustomersAsync(string searchTerm, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CustomerDto>?> GetAllCustomersAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetCustomerByIdAsync(Guid customerId, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetCustomerByPhoneAsync(string phone, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// User authorization information from backend
/// </summary>
public class UserAuthInfo
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? DefaultCompanyId { get; set; }
    public Guid? DefaultUserCompanyId { get; set; } // UserCompany ID for appointments
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsAuthorized { get; set; }
}

/// <summary>
/// Product DTO for backend communication
/// </summary>
public class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? StockQuantity { get; set; }
}

/// <summary>
/// Customer DTO for backend communication
/// </summary>
public class CustomerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
}
