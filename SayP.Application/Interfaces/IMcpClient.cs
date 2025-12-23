using SayP.Domain.Mcp;

namespace SayP.Application.Interfaces;

/// <summary>
/// MCP Client interface - JSON-RPC üzerinden MCP Server ile iletişim
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    /// <summary>
    /// Current connection state
    /// </summary>
    McpConnectionState State { get; }
    
    /// <summary>
    /// Server capabilities (available after connection)
    /// </summary>
    McpServerCapabilities? Capabilities { get; }
    
    /// <summary>
    /// Connect to an MCP server
    /// </summary>
    /// <param name="serverUrl">MCP server URL (HTTP/SSE endpoint)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Connection result with server capabilities</returns>
    Task<McpConnectionResult> ConnectAsync(
        string serverUrl, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnect from the server
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Check if server is reachable
    /// </summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
    
    #region Tools
    
    /// <summary>
    /// List available tools
    /// </summary>
    Task<List<McpTool>> ListToolsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Call a tool with arguments
    /// </summary>
    /// <param name="toolName">Name of the tool to call</param>
    /// <param name="arguments">Tool arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<McpToolResult> CallToolAsync(
        string toolName, 
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Resources
    
    /// <summary>
    /// List available resources
    /// </summary>
    Task<List<McpResource>> ListResourcesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Read a resource by URI
    /// </summary>
    /// <param name="resourceUri">Resource URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource content</returns>
    Task<McpResourceReadResult> ReadResourceAsync(
        string resourceUri,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Prompts
    
    /// <summary>
    /// List available prompts
    /// </summary>
    Task<List<McpPrompt>> ListPromptsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a prompt with arguments
    /// </summary>
    /// <param name="promptName">Name of the prompt</param>
    /// <param name="arguments">Prompt arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Prompt result with messages</returns>
    Task<McpPromptResult> GetPromptAsync(
        string promptName,
        Dictionary<string, string>? arguments = null,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Events
    
    /// <summary>
    /// Fired when tools list changes
    /// </summary>
    event EventHandler? ToolsListChanged;
    
    /// <summary>
    /// Fired when resources list changes
    /// </summary>
    event EventHandler? ResourcesListChanged;
    
    /// <summary>
    /// Fired when prompts list changes
    /// </summary>
    event EventHandler? PromptsListChanged;
    
    /// <summary>
    /// Fired when connection state changes
    /// </summary>
    event EventHandler<McpConnectionState>? ConnectionStateChanged;
    
    #endregion
}
