using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SayP.Application.Interfaces;

namespace SayP.Infrastructure.Mcp;

/// <summary>
/// MCP Infrastructure service registration
/// IMcpClient implementasyonunu (McpHttpClient) kaydeder
/// </summary>
public static class McpInfrastructureExtensions
{
    /// <summary>
    /// Add MCP Infrastructure services (IMcpClient implementation)
    /// Bu method, Application katmanındaki AddMcpServices'dan ÖNCE çağrılmalıdır
    /// </summary>
    public static IServiceCollection AddMcpInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mcpSection = configuration.GetSection("Mcp");
        var enabled = mcpSection.GetValue<bool>("Enabled");
        
        if (!enabled)
        {
            // MCP disabled - no infrastructure services needed
            return services;
        }
        
        var timeoutSeconds = mcpSection.GetValue<int>("ConnectionTimeoutSeconds", 30);
        
        // Register MCP HTTP Client
        services.AddHttpClient<IMcpClient, McpHttpClient>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        
        return services;
    }
}
