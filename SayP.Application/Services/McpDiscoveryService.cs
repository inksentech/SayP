using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Mcp;
using SayP.Domain.Models;

namespace SayP.Application.Services;

/// <summary>
/// MCP-based API discovery service
/// Mevcut ApiDiscoveryService'e paralel çalışır, onu DEĞİŞTİRMEZ
/// </summary>
public class McpDiscoveryService : IMcpDiscoveryService
{
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<McpDiscoveryService> _logger;
    
    // MCP endpoint suffix (backend'in MCP endpoint'i)
    private const string MCP_ENDPOINT_SUFFIX = "/mcp";
    
    public McpDiscoveryService(
        IMcpClient mcpClient,
        ILogger<McpDiscoveryService> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsMcpAvailableAsync(
        string baseUrl, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mcpUrl = GetMcpUrl(baseUrl);
            _logger.LogDebug("Checking MCP availability at {McpUrl}", mcpUrl);
            
            var result = await _mcpClient.ConnectAsync(mcpUrl, cancellationToken);
            
            if (result.Success)
            {
                await _mcpClient.DisconnectAsync();
                _logger.LogInformation("MCP server available at {McpUrl}", mcpUrl);
                return true;
            }
            
            _logger.LogDebug("MCP server not available at {McpUrl}: {Error}", 
                mcpUrl, result.ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MCP not available at {BaseUrl}", baseUrl);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<DiscoveredEndpoint>> DiscoverFromMcpAsync(
        string mcpUrl,
        CancellationToken cancellationToken = default)
    {
        var endpoints = new List<DiscoveredEndpoint>();
        
        try
        {
            _logger.LogInformation("Discovering endpoints from MCP server at {McpUrl}", mcpUrl);
            
            // Connect to MCP server
            var connectionResult = await _mcpClient.ConnectAsync(mcpUrl, cancellationToken);
            if (!connectionResult.Success)
            {
                _logger.LogWarning("Failed to connect to MCP server: {Error}", 
                    connectionResult.ErrorMessage);
                return endpoints;
            }
            
            // Check if server has tools capability
            if (!connectionResult.HasTools)
            {
                _logger.LogWarning("MCP server does not support tools");
                await _mcpClient.DisconnectAsync();
                return endpoints;
            }
            
            // List all tools
            var tools = await _mcpClient.ListToolsAsync(cancellationToken);
            
            _logger.LogInformation("Found {Count} MCP tools", tools.Count);
            
            // Convert MCP tools to DiscoveredEndpoints
            foreach (var tool in tools)
            {
                var endpoint = ConvertToolToEndpoint(tool);
                endpoints.Add(endpoint);
                
                _logger.LogDebug("Converted MCP tool '{ToolName}' to endpoint '{Intent}'", 
                    tool.Name, endpoint.Intent);
            }
            
            await _mcpClient.DisconnectAsync();
            
            _logger.LogInformation("Discovered {Count} endpoints from MCP server", endpoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering from MCP server at {McpUrl}", mcpUrl);
        }
        
        return endpoints;
    }

    /// <inheritdoc />
    public async Task<List<McpTool>> GetMcpToolsAsync(
        string mcpUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionResult = await _mcpClient.ConnectAsync(mcpUrl, cancellationToken);
            if (!connectionResult.Success || !connectionResult.HasTools)
            {
                return new List<McpTool>();
            }
            
            var tools = await _mcpClient.ListToolsAsync(cancellationToken);
            await _mcpClient.DisconnectAsync();
            
            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting MCP tools from {McpUrl}", mcpUrl);
            return new List<McpTool>();
        }
    }

    /// <inheritdoc />
    public async Task<List<McpResource>> GetMcpResourcesAsync(
        string mcpUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionResult = await _mcpClient.ConnectAsync(mcpUrl, cancellationToken);
            if (!connectionResult.Success || !connectionResult.HasResources)
            {
                return new List<McpResource>();
            }
            
            var resources = await _mcpClient.ListResourcesAsync(cancellationToken);
            await _mcpClient.DisconnectAsync();
            
            return resources;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting MCP resources from {McpUrl}", mcpUrl);
            return new List<McpResource>();
        }
    }

    /// <inheritdoc />
    public async Task<McpToolResult> ExecuteToolAsync(
        string mcpUrl,
        string toolName,
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionResult = await _mcpClient.ConnectAsync(mcpUrl, cancellationToken);
            if (!connectionResult.Success)
            {
                return new McpToolResult
                {
                    IsError = true,
                    Content = new List<McpContent>
                    {
                        new McpTextContent { Text = $"Failed to connect: {connectionResult.ErrorMessage}" }
                    }
                };
            }
            
            var result = await _mcpClient.CallToolAsync(toolName, arguments, cancellationToken);
            await _mcpClient.DisconnectAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MCP tool {ToolName}", toolName);
            return new McpToolResult
            {
                IsError = true,
                Content = new List<McpContent>
                {
                    new McpTextContent { Text = ex.Message }
                }
            };
        }
    }

    /// <inheritdoc />
    public async Task<McpResourceReadResult> ReadResourceAsync(
        string mcpUrl,
        string resourceUri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionResult = await _mcpClient.ConnectAsync(mcpUrl, cancellationToken);
            if (!connectionResult.Success)
            {
                return new McpResourceReadResult();
            }
            
            var result = await _mcpClient.ReadResourceAsync(resourceUri, cancellationToken);
            await _mcpClient.DisconnectAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading MCP resource {ResourceUri}", resourceUri);
            return new McpResourceReadResult();
        }
    }

    #region Conversion Helpers

    /// <summary>
    /// MCP Tool'u DiscoveredEndpoint'e dönüştür
    /// </summary>
    private DiscoveredEndpoint ConvertToolToEndpoint(McpTool tool)
    {
        var endpoint = new DiscoveredEndpoint
        {
            Id = $"mcp_{tool.Name}",
            Intent = tool.Name,
            Description = tool.Description ?? string.Empty,
            HttpMethod = "MCP", // MCP tool, REST değil
            Route = $"mcp://tools/{tool.Name}",
            ControllerName = "MCP",
            ActionName = tool.Name,
            Priority = 10, // MCP tools have higher priority
            RequiresConfirmation = true,
            DiscoveredAt = DateTime.UtcNow,
            Schema = ConvertSchemaToEndpointSchema(tool.InputSchema)
        };
        
        // Extract aliases from description if present
        // Format: "Description text [aliases: alias1, alias2]"
        if (!string.IsNullOrEmpty(tool.Description))
        {
            var aliasMatch = System.Text.RegularExpressions.Regex.Match(
                tool.Description, 
                @"\[aliases?:\s*([^\]]+)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (aliasMatch.Success)
            {
                endpoint.Aliases = aliasMatch.Groups[1].Value
                    .Split(',')
                    .Select(a => a.Trim())
                    .Where(a => !string.IsNullOrEmpty(a))
                    .ToArray();
                
                // Remove aliases from description
                endpoint.Description = tool.Description
                    .Replace(aliasMatch.Value, "")
                    .Trim();
            }
        }
        
        return endpoint;
    }

    /// <summary>
    /// MCP JSON Schema'yı EndpointSchema'ya dönüştür
    /// </summary>
    private EndpointSchema? ConvertSchemaToEndpointSchema(McpJsonSchema? mcpSchema)
    {
        if (mcpSchema?.Properties == null || !mcpSchema.Properties.Any())
            return null;
        
        var schema = new EndpointSchema
        {
            TypeName = "McpToolInput",
            Fields = new List<SchemaField>()
        };
        
        foreach (var (name, prop) in mcpSchema.Properties)
        {
            var field = new SchemaField
            {
                Name = name,
                Type = prop.Type,
                Description = prop.Description ?? string.Empty,
                IsRequired = mcpSchema.Required?.Contains(name) ?? false,
                IsOptional = !(mcpSchema.Required?.Contains(name) ?? false),
                DefaultValue = prop.Default,
                ValidationPattern = prop.Pattern
            };
            
            // Set example from schema
            if (prop.Examples?.Any() == true)
            {
                field.Example = prop.Examples.First()?.ToString() ?? string.Empty;
            }
            
            // Handle nested objects
            if (prop.Properties != null)
            {
                field.NestedFields = prop.Properties
                    .Select(p => new SchemaField
                    {
                        Name = p.Key,
                        Type = p.Value.Type,
                        Description = p.Value.Description ?? string.Empty,
                        IsRequired = prop.Required?.Contains(p.Key) ?? false
                    })
                    .ToList();
            }
            
            // Handle arrays
            if (prop.Type == "array" && prop.Items != null)
            {
                field.ItemType = prop.Items.Type;
            }
            
            schema.Fields.Add(field);
        }
        
        return schema;
    }

    /// <summary>
    /// Backend URL'den MCP URL oluştur
    /// </summary>
    private string GetMcpUrl(string baseUrl)
    {
        return baseUrl.TrimEnd('/') + MCP_ENDPOINT_SUFFIX;
    }

    #endregion
}
