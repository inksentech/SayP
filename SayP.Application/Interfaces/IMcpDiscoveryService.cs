using SayP.Domain.Mcp;
using SayP.Domain.Models;

namespace SayP.Application.Interfaces;

/// <summary>
/// MCP-based API discovery service
/// Mevcut IApiDiscoveryService'e paralel çalışır
/// </summary>
public interface IMcpDiscoveryService
{
    /// <summary>
    /// Check if MCP server is available at the given URL
    /// </summary>
    /// <param name="baseUrl">Backend base URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if MCP endpoint is available</returns>
    Task<bool> IsMcpAvailableAsync(
        string baseUrl, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Discover endpoints from MCP server
    /// </summary>
    /// <param name="mcpUrl">MCP server URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of discovered endpoints</returns>
    Task<List<DiscoveredEndpoint>> DiscoverFromMcpAsync(
        string mcpUrl,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get MCP tools as raw format (for AI tool calling)
    /// </summary>
    /// <param name="mcpUrl">MCP server URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of MCP tools</returns>
    Task<List<McpTool>> GetMcpToolsAsync(
        string mcpUrl,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get MCP resources
    /// </summary>
    /// <param name="mcpUrl">MCP server URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of MCP resources</returns>
    Task<List<McpResource>> GetMcpResourcesAsync(
        string mcpUrl,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute a tool via MCP
    /// </summary>
    /// <param name="mcpUrl">MCP server URL</param>
    /// <param name="toolName">Tool name</param>
    /// <param name="arguments">Tool arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<McpToolResult> ExecuteToolAsync(
        string mcpUrl,
        string toolName,
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Read a resource via MCP
    /// </summary>
    /// <param name="mcpUrl">MCP server URL</param>
    /// <param name="resourceUri">Resource URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource content</returns>
    Task<McpResourceReadResult> ReadResourceAsync(
        string mcpUrl,
        string resourceUri,
        CancellationToken cancellationToken = default);
}
