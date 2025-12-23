# SayP API Documentation

## Overview
SayP is an intelligent WhatsApp AI assistant that handles business operations through voice and text commands.

## Base URL
- **Development**: `http://localhost:5000`
- **Production**: `https://sayp.yourdomain.com`

## Authentication
WhatsApp webhook requests are authenticated using HMAC-SHA256 signatures.

### Webhook Signature Validation
```
X-Hub-Signature-256: sha256=<signature>
```

## Endpoints

### 1. Webhook Verification (GET)
Verifies WhatsApp webhook subscription.

**Endpoint**: `GET /api/webhook`

**Query Parameters**:
- `hub.mode` (string): Must be "subscribe"
- `hub.verify_token` (string): Verification token
- `hub.challenge` (string): Challenge string to return

**Response**: Returns the challenge string

**Example**:
```bash
curl "http://localhost:5000/api/webhook?hub.mode=subscribe&hub.verify_token=YOUR_TOKEN&hub.challenge=CHALLENGE_STRING"
```

---

### 2. Webhook Handler (POST)
Receives and processes WhatsApp messages.

**Endpoint**: `POST /api/webhook`

**Headers**:
- `Content-Type: application/json`
- `X-Hub-Signature-256: sha256=<signature>`

**Request Body**:
```json
{
  "object": "whatsapp_business_account",
  "entry": [{
    "id": "WHATSAPP_BUSINESS_ACCOUNT_ID",
    "changes": [{
      "value": {
        "messaging_product": "whatsapp",
        "metadata": {
          "display_phone_number": "PHONE_NUMBER",
          "phone_number_id": "PHONE_NUMBER_ID"
        },
        "messages": [{
          "from": "CUSTOMER_PHONE_NUMBER",
          "id": "MESSAGE_ID",
          "timestamp": "TIMESTAMP",
          "type": "text",
          "text": {
            "body": "Laptop ekle, 15000 TL"
          }
        }]
      }
    }]
  }]
}
```

**Response**: `200 OK`

---

### 3. Health Check (GET)
Checks the health status of the API and its dependencies.

**Endpoint**: `GET /health`

**Response**:
```json
{
  "status": "healthy",
  "timestamp": "2025-01-18T10:30:00Z",
  "checks": {
    "database": "healthy",
    "redis": "healthy"
  }
}
```

**Status Codes**:
- `200`: All systems healthy
- `503`: One or more systems unhealthy

---

### 4. Readiness Check (GET)
Checks if the API is ready to accept requests.

**Endpoint**: `GET /ready`

**Response**:
```json
{
  "status": "ready",
  "timestamp": "2025-01-18T10:30:00Z"
}
```

---

### 5. Metrics (GET)
Exposes Prometheus metrics for monitoring.

**Endpoint**: `GET /metrics`

**Response**: Prometheus format metrics

**Example Metrics**:
```
# HELP http_requests_total Total number of HTTP requests
# TYPE http_requests_total counter
http_requests_total{method="POST",endpoint="/api/webhook",status="200"} 1234

# HELP http_request_duration_seconds HTTP request duration
# TYPE http_request_duration_seconds histogram
http_request_duration_seconds_bucket{method="POST",endpoint="/api/webhook",le="0.1"} 1000
```

---

## Supported Commands

### Product Commands
- **Create Product**: "Laptop ekle, 15000 TL"
- **Update Product**: "Laptop fiyatını 16000 TL yap"
- **Delete Product**: "Laptop'ı sil"
- **List Products**: "Ürünleri listele"
- **Search Products**: "15000-20000 TL arası ürünler"

### Customer Commands
- **Create Customer**: "Ahmet Yılmaz müşteri ekle, tel: 0555 123 45 67"
- **List Customers**: "Müşterileri listele"

### Invoice Commands
- **Create Invoice**: "Ahmet Yılmaz'a fatura kes, 2 laptop"
- **List Invoices**: "Faturaları listele"

### Contract Commands
- **Create Contract**: "Ahmet Yılmaz ile 1 yıllık sözleşme yap"

---

## AI Features

### 1. Context Management
The AI remembers the last 5 messages in a conversation.

**Example**:
```
User: "Laptop ekle, 15000 TL"
AI: ✅ Laptop eklendi

User: "Fiyatını 16000 TL yap"  # No need to say "Laptop" again
AI: ✅ Laptop fiyatı güncellendi
```

### 2. Slot Filling
The AI asks for missing information.

**Example**:
```
User: "Ürün ekle"
AI: "Ürün adını belirtir misiniz?"

User: "Laptop"
AI: "Fiyatı ne kadar olacak?"

User: "15000 TL"
AI: ✅ Laptop oluşturuldu
```

### 3. Entity Extraction
The AI automatically extracts entities from messages:
- **Prices**: "15000 TL", "15.000 TL"
- **Quantities**: "10 adet", "5 tane"
- **Dates**: "bugün", "yarın", "2024-01-15"
- **Phone Numbers**: "+90 555 123 45 67"
- **Emails**: "user@example.com"
- **Tax Rates**: "KDV 18", "18%"

### 4. Intelligent Fallback
Multi-level fallback strategy:
1. **Primary AI** (Gemini 2.5 Flash)
2. **Secondary AI** (OpenAI GPT-4)
3. **Rule-based extraction**
4. **Clarification request**

---

## Rate Limiting
- **General**: 60 requests/minute
- **Webhook**: 100 requests/minute

**Response** (429 Too Many Requests):
```json
{
  "error": "Rate limit exceeded. Please try again later."
}
```

---

## Error Handling

### Error Response Format
```json
{
  "type": "https://httpstatuses.com/400",
  "title": "Bad Request",
  "status": 400,
  "detail": "Invalid request parameters.",
  "instance": "/api/webhook",
  "traceId": "00-abc123-def456-00"
}
```

### Common Error Codes
- `400 Bad Request`: Invalid request format
- `401 Unauthorized`: Invalid signature
- `429 Too Many Requests`: Rate limit exceeded
- `500 Internal Server Error`: Server error
- `503 Service Unavailable`: Service temporarily unavailable

---

## Monitoring

### Application Insights
Logs and telemetry are sent to Application Insights (if configured).

**Environment Variable**:
```
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=xxx;IngestionEndpoint=https://xxx
```

### Prometheus Metrics
Metrics are exposed at `/metrics` endpoint.

**Key Metrics**:
- `http_requests_total`: Total HTTP requests
- `http_request_duration_seconds`: Request duration
- `dotnet_total_memory_bytes`: Memory usage
- `process_cpu_seconds_total`: CPU usage

---

## Deployment

### Docker
```bash
docker build -t sayp-api -f SayP.Api/Dockerfile .
docker run -p 5000:8080 --env-file .env sayp-api
```

### Docker Compose
```bash
cd infra
docker-compose up -d
```

### Kubernetes
```bash
kubectl apply -f k8s/sayp-deployment.yml
```

---

## Environment Variables

### Required
- `WHATSAPP_PHONE_NUMBER_ID`: WhatsApp phone number ID
- `WHATSAPP_ACCESS_TOKEN`: WhatsApp API access token
- `WHATSAPP_WEBHOOK_VERIFY_TOKEN`: Webhook verification token
- `WHATSAPP_APP_SECRET`: App secret for signature validation
- `AI_PROVIDER`: AI provider (Gemini or OpenAI)
- `GEMINI_API_KEY` or `OPENAI_API_KEY`: AI API key

### Optional
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: Application Insights
- `BACKEND_API_URL`: Backend API URL (default: http://localhost:5245)
- `REDIS_CONNECTION_STRING`: Redis connection string
- `LOGGING_LEVEL`: Log level (default: Information)

---

## Support
For issues or questions, please contact the development team or create an issue on GitHub.
