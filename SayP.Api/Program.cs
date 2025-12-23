using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Text;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using SayP.Api;
using SayP.Api.Middleware;
using SayP.Application.Interfaces;
using SayP.Application.Services;
using SayP.Domain.Interfaces;
using SayP.Infrastructure.AI;
using SayP.Infrastructure.CommandExecution;
using SayP.Infrastructure.Persistence;
using SayP.Infrastructure.Redis;
using SayP.Infrastructure.WhatsApp;
using SayP.Infrastructure.Mcp;

// Configure Serilog
var appInsightsKey = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "SayP")
    .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/sayp-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

// Add Application Insights if configured
if (!string.IsNullOrEmpty(appInsightsKey))
{
    loggerConfig.WriteTo.ApplicationInsights(appInsightsKey, TelemetryConverter.Traces);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("Starting SayP API");

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog();

// Load environment variables from .env file
// SayP is now independent - loads its own .env from sayp folder
var possibleEnvPaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),           // sayp/SayP.Api/.env (development)
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),     // sayp/.env (standard location)
    Path.Combine(AppContext.BaseDirectory, ".env"),                  // bin folder (production)
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"), // sayp/.env from bin
};

var envLoaded = false;
foreach (var envPath in possibleEnvPaths)
{
    var fullPath = Path.GetFullPath(envPath);
    if (File.Exists(fullPath))
    {
        DotNetEnv.Env.Load(fullPath);
        Console.WriteLine($"✅ SayP .env loaded from: {fullPath}");
        envLoaded = true;
        break;
    }
}

if (!envLoaded)
{
    Console.WriteLine($"⚠️  No .env file found. Checked paths:");
    foreach (var p in possibleEnvPaths)
    {
        Console.WriteLine($"   - {Path.GetFullPath(p)}");
    }
    Console.WriteLine("   Using environment variables or appsettings.json instead.");
}

// Add configuration from environment variables
builder.Configuration.AddEnvironmentVariables();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR for Web Chat
builder.Services.AddSignalR();

// Add CORS for Web Chat (SignalR requires specific configuration)
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebChatPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true); // Allow SignalR negotiate
    });
});

// Add JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
{
    throw new InvalidOperationException("JWT configuration is missing in appsettings.json");
}

Log.Information("SayP Backend JWT Config - Key (first 10): {KeyStart}, Issuer: {Issuer}, Audience: {Audience}", 
    jwtKey.Substring(0, Math.Min(10, jwtKey.Length)), jwtIssuer, jwtAudience);

// Enable PII logging for debugging
Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // TAMAMEN KAPALI - SADECE TOKEN PARSE EDİLİYOR
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = false,
        RequireSignedTokens = false,
        SignatureValidator = (token, parameters) => new JsonWebToken(token), // Signature'ı atla
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
    
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Log.Error("JWT Authentication failed: {Error}", context.Exception);
            Log.Error("Token: {Token}", context.Request.Headers["Authorization"]);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var tenantId = context.Principal?.FindFirst("tenant_id")?.Value;
            Log.Information("JWT Token validated for tenant: {TenantId}", tenantId);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    connectionString = $"Host={Environment.GetEnvironmentVariable("SAYP_DB_HOST")};" +
                      $"Port={Environment.GetEnvironmentVariable("SAYP_DB_PORT")};" +
                      $"Database={Environment.GetEnvironmentVariable("SAYP_DB_NAME")};" +
                      $"Username={Environment.GetEnvironmentVariable("SAYP_DB_USER")};" +
                      $"Password={Environment.GetEnvironmentVariable("SAYP_DB_PASSWORD")}";
}

builder.Services.AddDbContext<SayPDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("SayP.Infrastructure")));

builder.Services.AddScoped<ISayPDbContext>(provider => provider.GetRequiredService<SayPDbContext>());

// Redis & Cache Service
var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "localhost:6379";
builder.Services.AddSingleton<RedisCacheService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
    return new RedisCacheService(redisConnection, logger);
});
// Register ICacheService interface for Clean Architecture
builder.Services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<RedisCacheService>());

// HttpClient for WhatsApp and Backend API
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>((sp, client) =>
{
    var accessToken = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
});

builder.Services.AddHttpClient<ICommandExecutor, BackendCommandExecutor>((sp, client) =>
{
    var backendUrl = Environment.GetEnvironmentVariable("BACKEND_API_URL") ?? "http://localhost:8080";
    client.BaseAddress = new Uri(backendUrl);
    client.Timeout = TimeSpan.FromSeconds(
        int.Parse(Environment.GetEnvironmentVariable("BACKEND_API_TIMEOUT_SECONDS") ?? "30")
    );
});

// Backend User Service for phone validation
builder.Services.AddHttpClient<SayP.Application.Interfaces.IBackendUserService, SayP.Infrastructure.Services.BackendUserService>();

// AI Provider
var aiProvider = Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "OpenAI";
if (aiProvider == "OpenAI")
{
    builder.Services.AddHttpClient<IAIProvider, OpenAIProvider>();
}
else if (aiProvider == "Gemini")
{
    builder.Services.AddHttpClient<IAIProvider, GeminiProvider>();
}
// Add other providers here (Anthropic, Azure OpenAI, etc.)

// ⚠️ DEPRECATED Application Services - Kept for backward compatibility
// These will be removed in v3.0.0. Use Generic AI services instead.
// builder.Services.AddScoped<AICommandRouter>(); // DEPRECATED: Use DynamicIntentMapper
#pragma warning disable CS0618 // Type or member is obsolete
builder.Services.AddScoped<SlotFillingManager>(); // Still used by SmartMessageProcessor (deprecated)
builder.Services.AddScoped<EntityExtractor>(); // ⚠️ DEPRECATED: Use DynamicSlotFiller
builder.Services.AddScoped<DialogueStateManager>(); // Still used for state management
builder.Services.AddScoped<IntelligentFallbackProvider>(); // Still used for fallback
builder.Services.AddScoped<SmartMessageProcessor>(); // ⚠️ DEPRECATED: Use GenericConversationManager
// builder.Services.AddScoped<ConversationManager>(); // ⚠️ DEPRECATED: Use GenericWhatsAppHandler
// builder.Services.AddScoped<ISmartConversationManager, SmartConversationManager>(); // ⚠️ DEPRECATED
#pragma warning restore CS0618 // Type or member is obsolete

builder.Services.AddScoped<WhatsAppSignatureValidator>(); // ✅ Still used for security
builder.Services.AddScoped<MessageRetryService>(); // ✅ Still used for reliability

// Advanced AI Services
builder.Services.AddScoped<IIntentDiscoveryService, IntentDiscoveryService>();
builder.Services.AddScoped<ISmartIntentClassifier, SmartIntentClassifier>();
builder.Services.AddScoped<IntentAnalyticsService>(); // ✨ NEW: Analytics Service
builder.Services.AddScoped<UserProfileService>(); // ✨ NEW: User Profile & Learning Service
builder.Services.AddScoped<TenantSettingsService>(); // ✨ NEW: Tenant Settings Management

// AI Enhancement Services (Phase 1-6)
builder.Services.AddSingleton<TurkishTypoCorrector>(); // Phase 1a: Turkish Typo Correction
builder.Services.AddSingleton<EnglishTypoCorrector>(); // Phase 1b: English Typo Correction
builder.Services.AddSingleton<MultiLanguageTypoCorrector>(); // Phase 1c: Multi-Language Typo Correction
builder.Services.AddScoped<IntentDisambiguator>(); // Phase 2: Intent Disambiguation
builder.Services.AddScoped<BackendSlotValidator>(); // Phase 3: Backend Validation
builder.Services.AddScoped<SmartContextManager>(); // Phase 4: Smart Context
builder.Services.AddScoped<ConfidenceCalibrator>(); // Phase 5: Confidence Calibration (Scoped - uses DbContext)
builder.Services.AddSingleton<PerformanceMonitor>(); // Phase 6: Performance Monitoring

// Smart Suggestion Services (Phase 7: Intelligent Suggestions)
builder.Services.AddSingleton<FuzzyMatchingService>(); // Fuzzy matching with Levenshtein distance
builder.Services.AddScoped<SmartSuggestionService>(); // Intelligent entity suggestions

// 🧠 SELF-LEARNING & AI ENHANCEMENT SERVICES (Phase 8)
builder.Services.AddScoped<SelfLearningService>(); // Core self-learning service
builder.Services.AddScoped<ContextManager>(); // Context & reference resolution
builder.Services.AddScoped<FeedbackService>(); // User feedback & correction handling
builder.Services.AddSingleton<MultiLanguageService>(); // Multi-language support (TR + EN)

// 🚀 GENERIC AI SERVICES - New Architecture
builder.Services.AddScoped<ApiDiscoveryService>(); // Base Swagger discovery (always available)
builder.Services.AddHttpClient<ApiDiscoveryService>(); // HttpClient for ApiDiscoveryService

// 🔌 MCP SERVICES - Parallel layer (does NOT change existing services)
// If Mcp:Enabled=true, CompositeDiscoveryService wraps ApiDiscoveryService
// If Mcp:Enabled=false, ApiDiscoveryService is used directly
builder.Services.AddMcpInfrastructure(builder.Configuration); // Infrastructure (IMcpClient)
builder.Services.AddMcpServices(builder.Configuration); // Application (IMcpDiscoveryService, CompositeDiscoveryService)
builder.Services.AddScoped<IDynamicIntentMapper, DynamicIntentMapper>();
builder.Services.AddScoped<IBackendTokenService, BackendTokenService>(); // Token generation for backend auth
builder.Services.AddScoped<IGenericCommandExecutor, GenericCommandExecutor>();
builder.Services.AddHttpClient<GenericCommandExecutor>(); // HttpClient for GenericCommandExecutor

// ✅ NEW: AI Navigation Resolver and Media Attachment services
builder.Services.AddScoped<AINavigationResolver>();
builder.Services.AddScoped<AIMediaAttachmentService>();

builder.Services.AddScoped<DynamicSlotFiller>(sp => 
    new DynamicSlotFiller(
        sp.GetRequiredService<ILogger<DynamicSlotFiller>>(),
        sp.GetRequiredService<IAIProvider>(),
        sp.GetRequiredService<AINavigationResolver>(),      // ✅ NEW: Navigation resolution
        sp.GetRequiredService<AIMediaAttachmentService>())); // ✅ NEW: Media attachment
builder.Services.AddScoped<EntityResolverService>(); // ✅ Entity resolution service
builder.Services.AddScoped<GenericConversationManager>();
builder.Services.AddScoped<GenericWhatsAppHandler>(); // WhatsApp integration

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Global Exception Handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Rate Limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<AspNetCoreRateLimit.IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.RealIpHeader = "X-Real-IP";
    options.ClientIdHeader = "X-ClientId";
    options.GeneralRules = new List<AspNetCoreRateLimit.RateLimitRule>
    {
        new AspNetCoreRateLimit.RateLimitRule
        {
            Endpoint = "*",
            Period = "1m",
            Limit = 60
        },
        new AspNetCoreRateLimit.RateLimitRule
        {
            Endpoint = "*/api/webhook",
            Period = "1m",
            Limit = 100
        }
    };
});
builder.Services.AddSingleton<AspNetCoreRateLimit.IIpPolicyStore, AspNetCoreRateLimit.MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<AspNetCoreRateLimit.IRateLimitCounterStore, AspNetCoreRateLimit.MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<AspNetCoreRateLimit.IRateLimitConfiguration, AspNetCoreRateLimit.RateLimitConfiguration>();
builder.Services.AddSingleton<AspNetCoreRateLimit.IProcessingStrategy, AspNetCoreRateLimit.AsyncKeyLockProcessingStrategy>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable request body buffering for signature verification
app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

// CORS must be before authentication and authorization
app.UseCors("WebChatPolicy");

// Prometheus Metrics
app.UseMetricServer(); // Exposes /metrics endpoint
app.UseHttpMetrics();  // Collects HTTP metrics

// Rate Limiting Middleware
app.UseMiddleware<AspNetCoreRateLimit.IpRateLimitMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Map SignalR Hub (CORS already applied)
app.MapHub<SayP.Api.Hubs.ChatHub>("/chatHub");

// Health check endpoints
app.MapGet("/health", async (SayPDbContext dbContext, RedisCacheService redis) =>
{
    try
    {
        // Check database
        var dbHealthy = await dbContext.Database.CanConnectAsync();
        
        // Check Redis
        var redisHealthy = await redis.HealthCheckAsync();
        
        var isHealthy = dbHealthy && redisHealthy;
        
        var response = new
        {
            status = isHealthy ? "healthy" : "unhealthy",
            timestamp = DateTime.UtcNow,
            checks = new
            {
                database = dbHealthy ? "healthy" : "unhealthy",
                redis = redisHealthy ? "healthy" : "unhealthy"
            }
        };
        
        return isHealthy 
            ? Results.Ok(response) 
            : Results.Json(response, statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "unhealthy",
            timestamp = DateTime.UtcNow,
            error = ex.Message
        }, statusCode: 503);
    }
})
.AllowAnonymous();

app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

    Log.Information("SayP API started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SayP API failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
