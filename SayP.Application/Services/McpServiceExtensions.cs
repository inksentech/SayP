using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

/// <summary>
/// MCP Service registration extensions
/// Mevcut servisleri DEĞİŞTİRMEDEN MCP desteği ekler
/// 
/// NOT: IMcpClient implementasyonu (McpHttpClient) Infrastructure katmanında.
/// Bu extension method sadece interface'leri ve Application katmanı servislerini kaydeder.
/// Infrastructure katmanındaki implementasyonlar ayrıca kaydedilmelidir.
/// </summary>
public static class McpServiceExtensions
{
    /// <summary>
    /// Add MCP services to the service collection
    /// Feature flag ile kontrol edilir - kapalıysa mevcut sistem aynen çalışır
    /// </summary>
    public static IServiceCollection AddMcpServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Load MCP options
        var mcpSection = configuration.GetSection(McpOptions.SectionName);
        services.Configure<McpOptions>(mcpSection);
        
        var mcpOptions = mcpSection.Get<McpOptions>() ?? new McpOptions();
        
        if (!mcpOptions.Enabled)
        {
            // MCP disabled - mevcut sistem aynen çalışır
            // IApiDiscoveryService olarak ApiDiscoveryService kullanılacak
            services.AddScoped<IApiDiscoveryService>(sp => sp.GetRequiredService<ApiDiscoveryService>());
            return services;
        }
        
        // MCP enabled - paralel katman olarak ekle
        // NOT: IMcpClient implementasyonu Program.cs'de Infrastructure katmanından kaydedilmeli
        
        // 1. MCP Discovery Service
        services.AddScoped<IMcpDiscoveryService, McpDiscoveryService>();
        
        // 2. Composite Discovery Service (MCP + Swagger birleşik)
        // Bu, mevcut IApiDiscoveryService kaydını OVERRIDE eder
        services.AddScoped<IApiDiscoveryService>(sp =>
        {
            var swaggerDiscovery = sp.GetRequiredService<ApiDiscoveryService>();
            var mcpDiscovery = sp.GetService<IMcpDiscoveryService>();
            var cache = sp.GetRequiredService<ICacheService>();
            var logger = sp.GetRequiredService<ILogger<CompositeDiscoveryService>>();
            var options = Microsoft.Extensions.Options.Options.Create(mcpOptions);
            
            return new CompositeDiscoveryService(
                swaggerDiscovery,
                cache,
                logger,
                options,
                mcpDiscovery);
        });
        
        return services;
    }
    
    /// <summary>
    /// Add MCP services with custom options
    /// </summary>
    public static IServiceCollection AddMcpServices(
        this IServiceCollection services,
        Action<McpOptions> configureOptions)
    {
        var options = new McpOptions();
        configureOptions(options);
        
        services.Configure<McpOptions>(opt =>
        {
            opt.Enabled = options.Enabled;
            opt.EndpointSuffix = options.EndpointSuffix;
            opt.ConnectionTimeoutSeconds = options.ConnectionTimeoutSeconds;
            opt.PreferMcp = options.PreferMcp;
            opt.CacheEnabled = options.CacheEnabled;
            opt.CacheDurationMinutes = options.CacheDurationMinutes;
        });
        
        if (!options.Enabled)
        {
            services.AddScoped<IApiDiscoveryService>(sp => sp.GetRequiredService<ApiDiscoveryService>());
            return services;
        }
        
        // MCP Discovery Service
        services.AddScoped<IMcpDiscoveryService, McpDiscoveryService>();
        
        // Composite Discovery Service
        services.AddScoped<IApiDiscoveryService>(sp =>
        {
            var swaggerDiscovery = sp.GetRequiredService<ApiDiscoveryService>();
            var mcpDiscovery = sp.GetService<IMcpDiscoveryService>();
            var cache = sp.GetRequiredService<ICacheService>();
            var logger = sp.GetRequiredService<ILogger<CompositeDiscoveryService>>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            
            return new CompositeDiscoveryService(
                swaggerDiscovery,
                cache,
                logger,
                opts,
                mcpDiscovery);
        });
        
        return services;
    }
}
