namespace SayP.Application.CommandSchemas;

/// <summary>
/// Schema for creating a customer
/// </summary>
public class CreateCustomerCommand
{
    /// <summary>
    /// Customer name (required)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Email address
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Phone number
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Tax ID / TC Kimlik No
    /// </summary>
    public string? TaxId { get; set; }

    /// <summary>
    /// Tax office
    /// </summary>
    public string? TaxOffice { get; set; }

    /// <summary>
    /// Address
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// City
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Country
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Notes
    /// </summary>
    public string? Notes { get; set; }
}
