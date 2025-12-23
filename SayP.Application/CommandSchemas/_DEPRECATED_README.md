# ⚠️ DEPRECATED - Command Schemas

## Status: DEPRECATED

This folder contains **hard-coded command schemas** that are **no longer used** in the Generic AI architecture.

## Why Deprecated?

In the new Generic AI architecture:
- ✅ Schemas are **automatically discovered** from Swagger/OpenAPI
- ✅ No manual schema definition needed
- ✅ Backend attributes (`[SayP]`, `[SayPField]`) provide metadata
- ✅ Dynamic schema extraction from DTO properties

## Migration

### Old Way (Hard-Coded)
```csharp
// Manual schema definition
public class CreateProductCommand
{
    public static string JsonSchema = @"{
        ""type"": ""object"",
        ""properties"": {
            ""name"": { ""type"": ""string"" }
        }
    }";
}
```

### New Way (Generic AI)
```csharp
// Backend'de sadece attribute
[HttpPost]
[SayP(Intent = "create_product", Description = "Ürün oluşturur")]
public async Task<IActionResult> Create([FromBody] Product product)

public class Product
{
    [SayPField(Description = "Ürün adı", Example = "Laptop")]
    public string Name { get; set; }
}

// SayP otomatik schema extract eder!
```

## Files in This Folder

All files in this folder are kept for **backward compatibility only** and will be removed in **v3.0.0**.

- ❌ `CreateProductCommand.cs` - Use auto-discovery
- ❌ `CreateInvoiceCommand.cs` - Use auto-discovery
- ❌ `CreateAppointmentCommand.cs` - Use auto-discovery
- ❌ `ListAppointmentsCommand.cs` - Use auto-discovery
- ❌ All other command schemas - Use auto-discovery

## What to Use Instead?

Use the new Generic AI services:
- `ApiDiscoveryService` - Auto-discovers endpoints
- `DynamicIntentMapper` - Maps intents dynamically
- `GenericCommandExecutor` - Executes any endpoint

## Removal Timeline

- **v2.0.0** (Current): Deprecated, kept for compatibility
- **v2.5.0**: Warning logs added when used
- **v3.0.0**: Complete removal

## Need Help?

See documentation:
- `MIGRATION_TO_GENERIC.md` - Migration guide
- `QUICK_START_GENERIC_AI.md` - Quick start
- `backend/SAYP_INTEGRATION_EXAMPLE.md` - Backend examples
