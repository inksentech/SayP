using System.Text.Json.Serialization;

namespace SayP.Domain.Mcp;

#region Core MCP Types

/// <summary>
/// MCP Tool - AI'ın çağırabileceği bir fonksiyon
/// </summary>
public class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("inputSchema")]
    public McpJsonSchema InputSchema { get; set; } = new();
}

/// <summary>
/// MCP Resource - AI'ın okuyabileceği bir veri kaynağı
/// </summary>
public class McpResource
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

/// <summary>
/// MCP Prompt - Önceden tanımlı prompt şablonu
/// </summary>
public class McpPrompt
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("arguments")]
    public List<McpPromptArgument>? Arguments { get; set; }
}

/// <summary>
/// MCP Prompt Argument
/// </summary>
public class McpPromptArgument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

#endregion

#region JSON Schema

/// <summary>
/// JSON Schema for MCP tool input
/// </summary>
public class McpJsonSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";
    
    [JsonPropertyName("properties")]
    public Dictionary<string, McpJsonSchemaProperty>? Properties { get; set; }
    
    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
    
    [JsonPropertyName("additionalProperties")]
    public bool? AdditionalProperties { get; set; }
}

/// <summary>
/// JSON Schema Property
/// </summary>
public class McpJsonSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("enum")]
    public List<string>? Enum { get; set; }
    
    [JsonPropertyName("default")]
    public object? Default { get; set; }
    
    [JsonPropertyName("examples")]
    public List<object>? Examples { get; set; }
    
    [JsonPropertyName("items")]
    public McpJsonSchemaProperty? Items { get; set; }
    
    [JsonPropertyName("properties")]
    public Dictionary<string, McpJsonSchemaProperty>? Properties { get; set; }
    
    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
    
    [JsonPropertyName("format")]
    public string? Format { get; set; }
    
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }
    
    [JsonPropertyName("minimum")]
    public double? Minimum { get; set; }
    
    [JsonPropertyName("maximum")]
    public double? Maximum { get; set; }
    
    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }
    
    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }
}

#endregion

#region Content Types

/// <summary>
/// MCP Content - Tool veya resource sonucu
/// </summary>
public abstract class McpContent
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Text content
/// </summary>
public class McpTextContent : McpContent
{
    [JsonPropertyName("type")]
    public override string Type => "text";
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Image content
/// </summary>
public class McpImageContent : McpContent
{
    [JsonPropertyName("type")]
    public override string Type => "image";
    
    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
    
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "image/png";
}

/// <summary>
/// Embedded resource content
/// </summary>
public class McpEmbeddedResource : McpContent
{
    [JsonPropertyName("type")]
    public override string Type => "resource";
    
    [JsonPropertyName("resource")]
    public McpResourceContent Resource { get; set; } = new();
}

/// <summary>
/// Resource content wrapper
/// </summary>
public class McpResourceContent
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
    
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
    
    [JsonPropertyName("text")]
    public string? Text { get; set; }
    
    [JsonPropertyName("blob")]
    public string? Blob { get; set; }
}

#endregion

#region Results

/// <summary>
/// Tool call result
/// </summary>
public class McpToolResult
{
    [JsonPropertyName("content")]
    public List<McpContent> Content { get; set; } = new();
    
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

/// <summary>
/// Resource read result
/// </summary>
public class McpResourceReadResult
{
    [JsonPropertyName("contents")]
    public List<McpResourceContent> Contents { get; set; } = new();
}

/// <summary>
/// Prompt get result
/// </summary>
public class McpPromptResult
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("messages")]
    public List<McpPromptMessage> Messages { get; set; } = new();
}

/// <summary>
/// Prompt message
/// </summary>
public class McpPromptMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";
    
    [JsonPropertyName("content")]
    public McpContent Content { get; set; } = new McpTextContent();
}

#endregion

#region Server Info

/// <summary>
/// MCP Server capabilities
/// </summary>
public class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public McpToolsCapability? Tools { get; set; }
    
    [JsonPropertyName("resources")]
    public McpResourcesCapability? Resources { get; set; }
    
    [JsonPropertyName("prompts")]
    public McpPromptsCapability? Prompts { get; set; }
    
    [JsonPropertyName("logging")]
    public McpLoggingCapability? Logging { get; set; }
}

public class McpToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

public class McpResourcesCapability
{
    [JsonPropertyName("subscribe")]
    public bool? Subscribe { get; set; }
    
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

public class McpPromptsCapability
{
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

public class McpLoggingCapability { }

/// <summary>
/// MCP Server info
/// </summary>
public class McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Initialize result
/// </summary>
public class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";
    
    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();
    
    [JsonPropertyName("serverInfo")]
    public McpServerInfo ServerInfo { get; set; } = new();
}

#endregion

#region Connection

/// <summary>
/// MCP Connection result
/// </summary>
public class McpConnectionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public McpInitializeResult? InitializeResult { get; set; }
    public McpServerCapabilities? Capabilities => InitializeResult?.Capabilities;
    public bool HasTools => Capabilities?.Tools != null;
    public bool HasResources => Capabilities?.Resources != null;
    public bool HasPrompts => Capabilities?.Prompts != null;
}

/// <summary>
/// MCP Connection state
/// </summary>
public enum McpConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

#endregion
