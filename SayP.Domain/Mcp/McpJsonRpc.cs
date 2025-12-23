using System.Text.Json;
using System.Text.Json.Serialization;

namespace SayP.Domain.Mcp;

/// <summary>
/// JSON-RPC 2.0 Request
/// </summary>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("id")]
    public object? Id { get; set; }
    
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
    
    /// <summary>
    /// Create a request with typed params
    /// </summary>
    public static JsonRpcRequest Create<T>(string method, T? parameters, object? id = null) where T : class
    {
        return new JsonRpcRequest
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Method = method,
            Params = parameters != null 
                ? JsonSerializer.SerializeToElement(parameters) 
                : null
        };
    }
    
    /// <summary>
    /// Create a notification (no id, no response expected)
    /// </summary>
    public static JsonRpcRequest CreateNotification<T>(string method, T? parameters) where T : class
    {
        return new JsonRpcRequest
        {
            Id = null,
            Method = method,
            Params = parameters != null 
                ? JsonSerializer.SerializeToElement(parameters) 
                : null
        };
    }
}

/// <summary>
/// JSON-RPC 2.0 Response
/// </summary>
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("id")]
    public object? Id { get; set; }
    
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }
    
    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
    
    public bool IsSuccess => Error == null;
    
    /// <summary>
    /// Get typed result
    /// </summary>
    public T? GetResult<T>()
    {
        if (Result == null) return default;
        return JsonSerializer.Deserialize<T>(Result.Value.GetRawText());
    }
}

/// <summary>
/// JSON-RPC 2.0 Error
/// </summary>
public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

/// <summary>
/// Standard JSON-RPC error codes
/// </summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    
    // MCP specific error codes (-32000 to -32099)
    public const int McpConnectionClosed = -32000;
    public const int McpRequestTimeout = -32001;
}

#region MCP Method Parameters

/// <summary>
/// Initialize request params
/// </summary>
public class McpInitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";
    
    [JsonPropertyName("capabilities")]
    public McpClientCapabilities Capabilities { get; set; } = new();
    
    [JsonPropertyName("clientInfo")]
    public McpClientInfo ClientInfo { get; set; } = new();
}

public class McpClientCapabilities
{
    [JsonPropertyName("roots")]
    public McpRootsCapability? Roots { get; set; }
    
    [JsonPropertyName("sampling")]
    public McpSamplingCapability? Sampling { get; set; }
}

public class McpRootsCapability
{
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

public class McpSamplingCapability { }

public class McpClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "SayP";
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
}

/// <summary>
/// Tools list params (empty for now)
/// </summary>
public class McpToolsListParams
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>
/// Tools list result
/// </summary>
public class McpToolsListResult
{
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = new();
    
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Tool call params
/// </summary>
public class McpToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Resources list params
/// </summary>
public class McpResourcesListParams
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>
/// Resources list result
/// </summary>
public class McpResourcesListResult
{
    [JsonPropertyName("resources")]
    public List<McpResource> Resources { get; set; } = new();
    
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Resource read params
/// </summary>
public class McpResourceReadParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Prompts list params
/// </summary>
public class McpPromptsListParams
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>
/// Prompts list result
/// </summary>
public class McpPromptsListResult
{
    [JsonPropertyName("prompts")]
    public List<McpPrompt> Prompts { get; set; } = new();
    
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Prompt get params
/// </summary>
public class McpPromptGetParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("arguments")]
    public Dictionary<string, string>? Arguments { get; set; }
}

#endregion

#region MCP Methods

/// <summary>
/// MCP Protocol method names
/// </summary>
public static class McpMethods
{
    // Lifecycle
    public const string Initialize = "initialize";
    public const string Initialized = "notifications/initialized";
    public const string Ping = "ping";
    
    // Tools
    public const string ToolsList = "tools/list";
    public const string ToolsCall = "tools/call";
    
    // Resources
    public const string ResourcesList = "resources/list";
    public const string ResourcesRead = "resources/read";
    public const string ResourcesSubscribe = "resources/subscribe";
    public const string ResourcesUnsubscribe = "resources/unsubscribe";
    
    // Prompts
    public const string PromptsList = "prompts/list";
    public const string PromptsGet = "prompts/get";
    
    // Logging
    public const string LoggingSetLevel = "logging/setLevel";
    
    // Notifications (server -> client)
    public const string NotificationToolsListChanged = "notifications/tools/list_changed";
    public const string NotificationResourcesListChanged = "notifications/resources/list_changed";
    public const string NotificationResourcesUpdated = "notifications/resources/updated";
    public const string NotificationPromptsListChanged = "notifications/prompts/list_changed";
    public const string NotificationProgress = "notifications/progress";
    public const string NotificationMessage = "notifications/message";
}

#endregion
