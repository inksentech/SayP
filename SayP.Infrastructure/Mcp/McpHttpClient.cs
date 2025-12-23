using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Mcp;

namespace SayP.Infrastructure.Mcp;

/// <summary>
/// HTTP-based MCP Client implementation
/// JSON-RPC 2.0 over HTTP/SSE
/// </summary>
public class McpHttpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpHttpClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    
    private string? _serverUrl;
    private McpServerCapabilities? _capabilities;
    private McpConnectionState _state = McpConnectionState.Disconnected;
    
    public McpConnectionState State => _state;
    public McpServerCapabilities? Capabilities => _capabilities;
    
    public event EventHandler? ToolsListChanged;
    public event EventHandler? ResourcesListChanged;
    public event EventHandler? PromptsListChanged;
    public event EventHandler<McpConnectionState>? ConnectionStateChanged;

    public McpHttpClient(
        HttpClient httpClient,
        ILogger<McpHttpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    #region Connection

    public async Task<McpConnectionResult> ConnectAsync(
        string serverUrl, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            SetState(McpConnectionState.Connecting);
            _serverUrl = serverUrl.TrimEnd('/');
            
            _logger.LogInformation("Connecting to MCP server at {ServerUrl}", _serverUrl);
            
            // Send initialize request
            var initParams = new McpInitializeParams
            {
                ProtocolVersion = "2024-11-05",
                ClientInfo = new McpClientInfo
                {
                    Name = "SayP",
                    Version = "1.0.0"
                },
                Capabilities = new McpClientCapabilities()
            };
            
            var response = await SendRequestAsync<McpInitializeResult>(
                McpMethods.Initialize, 
                initParams, 
                cancellationToken);
            
            if (response == null)
            {
                SetState(McpConnectionState.Error);
                return new McpConnectionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to initialize MCP connection"
                };
            }
            
            _capabilities = response.Capabilities;
            
            // Send initialized notification
            await SendNotificationAsync(McpMethods.Initialized, null, cancellationToken);
            
            SetState(McpConnectionState.Connected);
            
            _logger.LogInformation(
                "Connected to MCP server: {ServerName} v{Version} (Tools: {HasTools}, Resources: {HasResources}, Prompts: {HasPrompts})",
                response.ServerInfo?.Name ?? "Unknown",
                response.ServerInfo?.Version ?? "Unknown",
                _capabilities?.Tools != null,
                _capabilities?.Resources != null,
                _capabilities?.Prompts != null);
            
            return new McpConnectionResult
            {
                Success = true,
                InitializeResult = response
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server at {ServerUrl}", serverUrl);
            SetState(McpConnectionState.Error);
            
            return new McpConnectionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task DisconnectAsync()
    {
        _serverUrl = null;
        _capabilities = null;
        SetState(McpConnectionState.Disconnected);
        
        _logger.LogInformation("Disconnected from MCP server");
        return Task.CompletedTask;
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_state != McpConnectionState.Connected)
                return false;
            
            var response = await SendRequestAsync<object>(McpMethods.Ping, null, cancellationToken);
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Tools

    public async Task<List<McpTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        
        var allTools = new List<McpTool>();
        string? cursor = null;
        
        do
        {
            var result = await SendRequestAsync<McpToolsListResult>(
                McpMethods.ToolsList,
                new McpToolsListParams { Cursor = cursor },
                cancellationToken);
            
            if (result?.Tools != null)
            {
                allTools.AddRange(result.Tools);
            }
            
            cursor = result?.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
        
        _logger.LogDebug("Listed {Count} tools from MCP server", allTools.Count);
        return allTools;
    }

    public async Task<McpToolResult> CallToolAsync(
        string toolName, 
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        
        _logger.LogInformation("Calling MCP tool: {ToolName} with {ArgCount} arguments", 
            toolName, arguments?.Count ?? 0);
        
        var result = await SendRequestAsync<McpToolResult>(
            McpMethods.ToolsCall,
            new McpToolCallParams
            {
                Name = toolName,
                Arguments = arguments
            },
            cancellationToken);
        
        if (result == null)
        {
            return new McpToolResult
            {
                IsError = true,
                Content = new List<McpContent>
                {
                    new McpTextContent { Text = "Failed to call tool" }
                }
            };
        }
        
        _logger.LogInformation("MCP tool {ToolName} completed. IsError: {IsError}", 
            toolName, result.IsError);
        
        return result;
    }

    #endregion

    #region Resources

    public async Task<List<McpResource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        
        var allResources = new List<McpResource>();
        string? cursor = null;
        
        do
        {
            var result = await SendRequestAsync<McpResourcesListResult>(
                McpMethods.ResourcesList,
                new McpResourcesListParams { Cursor = cursor },
                cancellationToken);
            
            if (result?.Resources != null)
            {
                allResources.AddRange(result.Resources);
            }
            
            cursor = result?.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
        
        _logger.LogDebug("Listed {Count} resources from MCP server", allResources.Count);
        return allResources;
    }

    public async Task<McpResourceReadResult> ReadResourceAsync(
        string resourceUri,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        
        _logger.LogDebug("Reading MCP resource: {ResourceUri}", resourceUri);
        
        var result = await SendRequestAsync<McpResourceReadResult>(
            McpMethods.ResourcesRead,
            new McpResourceReadParams { Uri = resourceUri },
            cancellationToken);
        
        return result ?? new McpResourceReadResult();
    }

    #endregion

    #region Prompts

    public async Task<List<McpPrompt>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        
        var allPrompts = new List<McpPrompt>();
        string? cursor = null;
        
        do
        {
            var result = await SendRequestAsync<McpPromptsListResult>(
                McpMethods.PromptsList,
                new McpPromptsListParams { Cursor = cursor },
                cancellationToken);
            
            if (result?.Prompts != null)
            {
                allPrompts.AddRange(result.Prompts);
            }
            
            cursor = result?.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
        
        _logger.LogDebug("Listed {Count} prompts from MCP server", allPrompts.Count);
        return allPrompts;
    }

    public async Task<McpPromptResult> GetPromptAsync(
        string promptName,
        Dictionary<string, string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        
        _logger.LogDebug("Getting MCP prompt: {PromptName}", promptName);
        
        var result = await SendRequestAsync<McpPromptResult>(
            McpMethods.PromptsGet,
            new McpPromptGetParams
            {
                Name = promptName,
                Arguments = arguments
            },
            cancellationToken);
        
        return result ?? new McpPromptResult();
    }

    #endregion

    #region JSON-RPC Communication

    private async Task<TResult?> SendRequestAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken cancellationToken) where TResult : class
    {
        if (string.IsNullOrEmpty(_serverUrl))
            throw new InvalidOperationException("Not connected to MCP server");
        
        var request = new JsonRpcRequest
        {
            Id = Guid.NewGuid().ToString(),
            Method = method,
            Params = parameters != null 
                ? JsonSerializer.SerializeToElement(parameters, _jsonOptions) 
                : null
        };
        
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        _logger.LogDebug("MCP Request: {Method} -> {Url}", method, _serverUrl);
        
        var response = await _httpClient.PostAsync(_serverUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, _jsonOptions);
        
        if (rpcResponse == null)
        {
            _logger.LogWarning("Received null response for method {Method}", method);
            return null;
        }
        
        if (rpcResponse.Error != null)
        {
            _logger.LogError("MCP Error [{Code}]: {Message}", 
                rpcResponse.Error.Code, rpcResponse.Error.Message);
            throw new McpException(rpcResponse.Error.Code, rpcResponse.Error.Message);
        }
        
        return rpcResponse.GetResult<TResult>();
    }

    private async Task SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_serverUrl))
            return;
        
        var request = new JsonRpcRequest
        {
            Id = null, // Notifications have no ID
            Method = method,
            Params = parameters != null 
                ? JsonSerializer.SerializeToElement(parameters, _jsonOptions) 
                : null
        };
        
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        _logger.LogDebug("MCP Notification: {Method}", method);
        
        try
        {
            await _httpClient.PostAsync(_serverUrl, content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification {Method}", method);
        }
    }

    #endregion

    #region Helpers

    private void EnsureConnected()
    {
        if (_state != McpConnectionState.Connected)
        {
            throw new InvalidOperationException("Not connected to MCP server");
        }
    }

    private void SetState(McpConnectionState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            ConnectionStateChanged?.Invoke(this, newState);
        }
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(DisconnectAsync());
    }

    #endregion
}

/// <summary>
/// MCP specific exception
/// </summary>
public class McpException : Exception
{
    public int ErrorCode { get; }
    
    public McpException(int errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
