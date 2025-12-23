# SayP - WhatsApp Integration & AI Layer

Enterprise-grade WhatsApp Cloud API integration with AI-powered command extraction for multi-tenant applications.

## 🎯 Features

- ✅ **WhatsApp Cloud API Integration**
  - Webhook handling with signature verification
  - Idempotency support (duplicate message prevention)
  - Retry mechanism with exponential backoff
  - 24-hour messaging window tracking

- ✅ **AI Command Router**
  - Provider-agnostic AI interface (OpenAI, Anthropic, Azure OpenAI)
  - Natural language → JSON command extraction
  - Confidence scoring
  - Context-aware conversations

- ✅ **Command Execution**
  - JSON Schema validation
  - Supported commands: CreateProduct, CreateInvoice, CreateContract
  - Interactive confirmations (buttons/lists)
  - Async execution with queue

- ✅ **Multi-Tenant Support**
  - WhatsApp number ↔ Tenant mapping
  - Company-level isolation
  - Tenant-specific configurations

- ✅ **Logging & Monitoring**
  - Conversation logs
  - Command execution logs
  - Error tracking
  - Rate limiting logs
  - Admin UI for viewing logs

- ✅ **Production-Ready**
  - Redis for caching & queuing
  - EF Core with PostgreSQL
  - Docker Compose setup
  - Health checks
  - Structured logging

---

## 🏗️ Architecture

```
SayP/
├── SayP.Domain/          # Entities, Enums, Interfaces
├── SayP.Application/     # Business Logic, DTOs, Services
├── SayP.Infrastructure/  # External Integrations (WhatsApp, AI, Redis)
└── SayP.Api/            # API & Webhooks
```

### Clean Architecture Layers:

1. **Domain** - Core business entities and interfaces
2. **Application** - Use cases, command schemas, services
3. **Infrastructure** - External service implementations
4. **API** - HTTP endpoints and webhooks

---

## 🚀 Quick Start

### Prerequisites

- .NET 9.0 SDK
- Docker & Docker Compose
- WhatsApp Business Account
- AI Provider API Key (OpenAI/Anthropic/Azure)

### 1. Clone & Setup

```bash
cd multitenant-app/sayp
cp .env.example .env
```

### 2. Configure Environment Variables

Edit `.env`:

```env
# WhatsApp Cloud API
WHATSAPP_API_URL=https://graph.facebook.com/v18.0
WHATSAPP_PHONE_NUMBER_ID=your_phone_number_id
WHATSAPP_BUSINESS_ACCOUNT_ID=your_business_account_id
WHATSAPP_ACCESS_TOKEN=your_access_token
WHATSAPP_WEBHOOK_VERIFY_TOKEN=your_verify_token
WHATSAPP_APP_SECRET=your_app_secret

# AI Provider (choose one)
AI_PROVIDER=OpenAI  # OpenAI, Anthropic, AzureOpenAI
OPENAI_API_KEY=your_openai_key
OPENAI_MODEL=gpt-4-turbo-preview

# Database
DATABASE_CONNECTION_STRING=Host=postgres;Database=sayp;Username=postgres;Password=postgres

# Redis
REDIS_CONNECTION_STRING=redis:6379

# Main Backend API
BACKEND_API_URL=http://backend-api:5000
BACKEND_API_KEY=your_backend_api_key

# Logging
LOG_LEVEL=Information
```

### 3. Start Services

```bash
docker-compose up -d
```

This starts:
- SayP API (port 5100)
- PostgreSQL (port 5432)
- Redis (port 6379)
- Main Backend API (port 5000)

### 4. Run Migrations

```bash
dotnet ef database update --project SayP.Infrastructure --startup-project SayP.Api
```

### 5. Configure WhatsApp Webhook

1. Go to Meta Developer Console
2. Configure webhook URL: `https://your-domain.com/api/webhook/whatsapp`
3. Set verify token (same as `WHATSAPP_WEBHOOK_VERIFY_TOKEN`)
4. Subscribe to messages, message_status events

---

## 📡 API Endpoints

### Webhooks

#### `GET /api/webhook/whatsapp`
WhatsApp webhook verification

#### `POST /api/webhook/whatsapp`
Receive WhatsApp messages

### Admin

#### `GET /api/conversations`
List all conversations

#### `GET /api/conversations/{id}`
Get conversation details

#### `GET /api/conversations/{id}/messages`
Get conversation messages

#### `GET /api/commands`
List all commands

#### `GET /api/commands/{id}`
Get command details

---

## 🤖 AI Command Extraction

### Supported Commands

#### 1. CreateProduct
```
User: "Yeni ürün ekle: Laptop, fiyat 15000 TL"
AI extracts:
{
  "commandType": "CreateProduct",
  "command": {
    "name": "Laptop",
    "price": 15000,
    "unit": "adet"
  }
}
```

#### 2. CreateInvoice
```
User: "ABC Şirketi için fatura kes: 5 adet laptop 15000 TL"
AI extracts:
{
  "commandType": "CreateInvoice",
  "command": {
    "customerName": "ABC Şirketi",
    "items": [
      {
        "description": "Laptop",
        "quantity": 5,
        "unitPrice": 15000
      }
    ]
  }
}
```

#### 3. CreateContract
```
User: "Yeni sözleşme oluştur: ABC Ltd ile 1 yıllık bakım sözleşmesi"
AI extracts:
{
  "commandType": "CreateContract",
  "command": {
    "partyBName": "ABC Ltd",
    "type": "Bakım Sözleşmesi",
    "duration": "1 yıl"
  }
}
```

---

## 🔐 Security

### Webhook Signature Verification

All incoming webhooks are verified using HMAC-SHA256:

```csharp
var signature = Request.Headers["X-Hub-Signature-256"];
var isValid = _whatsAppService.VerifyWebhookSignature(payload, signature, appSecret);
```

### Idempotency

Duplicate messages are detected using WhatsApp message IDs:

```csharp
var existing = await _context.Messages
    .FirstOrDefaultAsync(m => m.WhatsAppMessageId == messageId);
if (existing != null) return Ok(); // Already processed
```

### Rate Limiting

Redis-based rate limiting per phone number:

```csharp
var key = $"ratelimit:{phoneNumber}";
var count = await _redis.IncrementAsync(key);
if (count > 10) return TooManyRequests();
```

---

## 🔄 24-Hour Messaging Window

WhatsApp allows free-form messages within 24 hours of user's last message.

### Within Window
- Send any text message
- Send interactive buttons/lists
- No template required

### Outside Window
- Must use approved templates
- Limited to specific use cases
- Requires template approval from Meta

### Implementation

```csharp
var conversation = await _conversationManager.GetOrCreateConversationAsync(phoneNumber, tenantId);

if (conversation.IsWithinWindow)
{
    // Send free-form message
    await _whatsAppService.SendTextMessageAsync(phoneNumber, message);
}
else
{
    // Send template message
    await _whatsAppService.SendTemplateMessageAsync(phoneNumber, "template_name", "en");
}
```

---

## 🧪 Testing

### Test Webhook Locally

Use ngrok to expose local server:

```bash
ngrok http 5100
```

Update webhook URL in Meta console to ngrok URL.

### Test AI Extraction

```bash
curl -X POST http://localhost:5100/api/test/extract \
  -H "Content-Type: application/json" \
  -d '{"message": "Yeni ürün ekle: Laptop 15000 TL"}'
```

### Test Command Execution

```bash
curl -X POST http://localhost:5100/api/test/execute \
  -H "Content-Type: application/json" \
  -d '{
    "commandType": "CreateProduct",
    "commandJson": "{\"name\":\"Laptop\",\"price\":15000}",
    "tenantId": "your-tenant-id"
  }'
```

---

## 📊 Monitoring

### Health Checks

```bash
curl http://localhost:5100/health
```

### Logs

View logs in real-time:

```bash
docker-compose logs -f sayp-api
```

### Admin UI

Access admin UI: `http://localhost:5100/admin`

- View conversations
- Monitor commands
- Check error logs
- View rate limits

---

## 🔧 Configuration

### AI Provider Configuration

#### OpenAI
```env
AI_PROVIDER=OpenAI
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-4-turbo-preview
OPENAI_MAX_TOKENS=1000
```

#### Anthropic
```env
AI_PROVIDER=Anthropic
ANTHROPIC_API_KEY=sk-ant-...
ANTHROPIC_MODEL=claude-3-sonnet-20240229
```

#### Azure OpenAI
```env
AI_PROVIDER=AzureOpenAI
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_KEY=your-key
AZURE_OPENAI_DEPLOYMENT=gpt-4
```

### Redis Configuration

```env
REDIS_CONNECTION_STRING=redis:6379
REDIS_PASSWORD=your-password
REDIS_DATABASE=0
REDIS_SSL=false
```

---

## 🐛 Troubleshooting

### Webhook not receiving messages

1. Check webhook URL is publicly accessible
2. Verify webhook token matches
3. Check Meta Developer Console for errors
4. Verify signature verification is working

### AI extraction failing

1. Check AI provider API key
2. Verify model name is correct
3. Check API rate limits
4. Review logs for detailed errors

### Commands not executing

1. Verify backend API is accessible
2. Check backend API key
3. Verify tenant mapping exists
4. Review command execution logs

---

## 📚 Additional Resources

- [WhatsApp Cloud API Documentation](https://developers.facebook.com/docs/whatsapp/cloud-api)
- [OpenAI API Documentation](https://platform.openai.com/docs)
- [Clean Architecture Guide](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)

---

## 📝 License

MIT License - See LICENSE file for details

---

## 🤝 Contributing

Contributions welcome! Please read CONTRIBUTING.md first.

---

## 📧 Support

For support, email support@sayp.com or open an issue.
