namespace SayP.Domain.Enums;

/// <summary>
/// DEPRECATED: This enum is no longer used in Generic AI architecture.
/// Use DiscoveredEndpoint.Intent (string) instead.
/// This file is kept for backward compatibility only.
/// </summary>
[Obsolete("CommandType enum is deprecated. Use DiscoveredEndpoint.Intent (string) instead. This will be removed in v3.0.0")]
public enum CommandType
{
    Unknown = 0,
    
    // Product Commands
    CreateProduct = 1,
    UpdateProduct = 2,
    DeleteProduct = 3,
    GetProduct = 4,
    ListProducts = 5,
    SearchProducts = 6,
    GetProductByCode = 7,
    GetProductsByCodes = 8,
    GetTodaysProducts = 9,
    
    // Customer Commands
    CreateCustomer = 10,
    UpdateCustomer = 11,
    DeleteCustomer = 12,
    GetCustomer = 13,
    ListCustomers = 14,
    SearchCustomers = 15,
    
    // Invoice Commands
    CreateInvoice = 20,
    UpdateInvoice = 21,
    DeleteInvoice = 22,
    GetInvoice = 23,
    ListInvoices = 24,
    GetInvoiceStatus = 25,
    SendInvoice = 26,
    
    // Contract Commands
    CreateContract = 30,
    UpdateContract = 31,
    DeleteContract = 32,
    GetContract = 33,
    ListContracts = 34,
    
    // Appointment Commands
    CreateAppointment = 35,
    UpdateAppointment = 36,
    CancelAppointment = 37,
    GetAppointment = 38,
    ListAppointments = 39,
    CheckAvailability = 46,
    GetAvailableSlots = 47,
    ListTodayAppointments = 48,
    ListAppointmentsByDateRange = 49,
    ListAppointmentsByTimeRange = 50,
    CancelAppointmentsByTimeRange = 51,
    
    // Analytics & Reports
    GetSalesReport = 60,
    GetCustomerReport = 61,
    GetProductReport = 62,
    GetFinancialSummary = 63,
    
    // Bulk Operations
    BulkCreateProducts = 70,
    BulkUpdateProducts = 71,
    BulkDeleteProducts = 72,
    
    // Smart Operations
    SuggestProducts = 80,
    PriceOptimization = 81,
    StockAlert = 82
}
