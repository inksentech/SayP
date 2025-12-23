# SayP - AI-Powered WhatsApp Integration Layer

SayP is a standalone AI middleware that connects WhatsApp to **any backend API** using natural language processing. It enables users to interact with your backend through WhatsApp messages in Turkish or English.

## 🎯 What SayP Does

```
User (WhatsApp): "yarın saat 3'te Ahmet Bey'e randevu al"
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                         SAYP                                 │
│                                                              │
│  ✅ Language detection (Turkish)                            │
│  ✅ Typo correction ("randevi" → "randevu")                 │
│  ✅ Intent detection → "create_appointment"                 │
│  ✅ Entity resolution → "Ahmet Bey" → Customer ID           │
│  ✅ Date parsing → "yarın saat 3" → 2024-12-05T15:00       │
│  ✅ Slot filling → Ask for missing fields                   │
│  ✅ Confirmation → "Onaylıyor musunuz?"                     │
│  ✅ Learning → Remember this pattern                        │
│                                                              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    Your Backend API
                    POST /api/appointments
```

## 🚀 Quick Start

### 1. Copy SayP folder to your project

```bash
cp -r sayp/ /path/to/your/project/sayp/
```

### 2. Configure environment

```bash
cd sayp
cp .env.example .env
# Edit .env with your values
```

### 3. Update Backend URL

In `.env`:
```env
BACKEND_API_URL=http://your-backend-api:port
```

### 4. Run SayP

```bash
cd SayP.Api
dotnet run
```

## 📋 Requirements

### Your Backend API Must Have:

1. **Swagger/OpenAPI** endpoint at `/swagger/v1/swagger.json`
2. **[SayP] attributes** on endpoints you want to expose (optional but recommended)

### Example Backend Controller:

```csharp
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    [HttpPost]
    [SayP(
        Intent = "create_customer",
        Description = "Create a new customer",
        Aliases = new[] { "müşteri ekle", "yeni müşteri", "add customer" }
    )]
    public async Task<IActionResult> Create([FromBody] CreateCustomerDto dto)
    {
        // Your logic
    }
}
```

### SayP Attributes (Optional):

```csharp
// In your Domain/Attributes folder
[AttributeUsage(AttributeTargets.Method)]
public class SayPAttribute : Attribute
{
    public string Intent { get; set; }
    public string Description { get; set; }
    public string[] Aliases { get; set; }
    public int Priority { get; set; }
    public bool RequiresConfirmation { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Property)]
public class SayPFieldAttribute : Attribute
{
    public string Description { get; set; }
    public string Example { get; set; }
    public bool Optional { get; set; }
    public string[] Aliases { get; set; }
}
```

## 🔧 Configuration

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `BACKEND_API_URL` | Your backend API URL | ✅ |
| `SAYP_DB_*` | SayP database connection | ✅ |
| `REDIS_CONNECTION_STRING` | Redis for caching | ✅ |
| `WHATSAPP_*` | WhatsApp Cloud API credentials | ✅ |
| `AI_PROVIDER` | `OpenAI` or `Gemini` | ✅ |
| `GEMINI_API_KEY` / `OPENAI_API_KEY` | AI provider API key | ✅ |

### MCP Support (Optional)

If your backend supports MCP (Model Context Protocol):

```env
MCP_ENABLED=true
MCP_ENDPOINT_SUFFIX=/mcp
```

## 📁 Project Structure

```
sayp/
├── SayP.Api/           # Web API, controllers, middleware
├── SayP.Application/   # Business logic, services, interfaces
├── SayP.Domain/        # Entities, models, domain logic
├── SayP.Infrastructure/# Database, Redis, AI providers, WhatsApp
├── .env                # Your configuration (gitignored)
├── .env.example        # Configuration template
└── README.md           # This file
```

## 🔌 Integration Steps

### Step 1: Database Setup

```sql
-- Create SayP database
CREATE DATABASE sayp_db;
```

Run migrations:
```bash
cd SayP.Api
dotnet ef database update --project ../SayP.Infrastructure
```

### Step 2: WhatsApp Webhook

Configure your WhatsApp webhook URL:
```
https://your-sayp-domain.com/api/webhook/whatsapp
```

### Step 3: Test

```bash
# Health check
curl http://localhost:5001/health

# Swagger UI
open http://localhost:5001/swagger
```

## 🌟 Features

- **Multi-language**: Turkish & English support
- **Typo tolerance**: Fuzzy matching for user input
- **Context awareness**: "o müşteriye fatura kes" (invoice that customer)
- **Self-learning**: Learns from successful interactions
- **Entity resolution**: Natural language → Entity IDs
- **Slot filling**: Asks for missing required fields
- **Confirmation**: Confirms before destructive actions

## 📊 Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  WhatsApp   │────▶│    SayP     │────▶│  Backend    │
│   User      │◀────│  Middleware │◀────│    API      │
└─────────────┘     └─────────────┘     └─────────────┘
                           │
                    ┌──────┴──────┐
                    │             │
               ┌────▼────┐  ┌────▼────┐
               │  Gemini │  │  Redis  │
               │   AI    │  │  Cache  │
               └─────────┘  └─────────┘
```

## 🔒 Security

- JWT authentication for API endpoints
- WhatsApp signature verification
- Rate limiting per phone number
- Tenant isolation for multi-tenant backends

## 📝 License

MIT License - See LICENSE file for details.

## 🤝 Support

For issues and questions, please open a GitHub issue.
