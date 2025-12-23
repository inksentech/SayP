using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SayP.Application.Interfaces;
using SayP.Domain.Models;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Generic executor that can call any discovered endpoint dynamically
/// </summary>
public class GenericCommandExecutor : IGenericCommandExecutor
{
    private readonly HttpClient _httpClient;
    private readonly IBackendTokenService _tokenService;
    private readonly ILogger<GenericCommandExecutor> _logger;

    public GenericCommandExecutor(
        HttpClient httpClient,
        IBackendTokenService tokenService,
        ILogger<GenericCommandExecutor> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<GenericExecutionResult> ExecuteAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> parameters,
        Guid tenantId,
        string baseUrl,
        string? phoneNumber = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Executing endpoint {Intent} for tenant {TenantId}", 
                endpoint.Intent, tenantId);

            // Validate parameters
            var validation = await ValidateParametersAsync(endpoint, parameters);
            if (!validation.IsValid)
            {
                return new GenericExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Validation failed: {string.Join(", ", validation.Errors)}",
                    ExecutionTime = stopwatch.Elapsed
                };
            }

            // Generate backend token if phoneNumber provided
            string? backendToken = null;
            if (!string.IsNullOrEmpty(phoneNumber))
            {
                try
                {
                    backendToken = await _tokenService.GenerateBackendTokenAsync(phoneNumber, tenantId);
                    _logger.LogInformation("Generated backend token for {Phone} / Tenant {TenantId}", phoneNumber, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate backend token for {Phone} / Tenant {TenantId}", phoneNumber, tenantId);
                }
            }

            // Build request
            var request = await BuildHttpRequestAsync(
                endpoint, 
                validation.ValidatedParameters, 
                baseUrl, 
                tenantId, 
                backendToken,
                apiKey);

            // ✅ IMPROVED: Execute request with retry mechanism
            var response = await ExecuteWithRetryAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            stopwatch.Stop();

            // Parse response
            object? parsedResponse = null;
            try
            {
                if (!string.IsNullOrEmpty(responseBody))
                {
                    parsedResponse = JsonConvert.DeserializeObject(responseBody);
                }
            }
            catch
            {
                // Response might not be JSON, keep as string
                parsedResponse = responseBody;
            }

            var result = new GenericExecutionResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseBody = responseBody,
                ParsedResponse = parsedResponse,
                ExecutionTime = stopwatch.Elapsed
            };

            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                _logger.LogWarning("Endpoint execution failed: {StatusCode} - {Body}", 
                    response.StatusCode, responseBody);
            }
            else
            {
                _logger.LogInformation("Endpoint executed successfully in {Duration}ms", 
                    stopwatch.ElapsedMilliseconds);
                
                // 🐛 DEBUG: Log response body to see what we're getting
                _logger.LogInformation("📦 Response body (first 500 chars): {ResponseBody}", 
                    responseBody?.Length > 500 ? responseBody.Substring(0, 500) + "..." : responseBody);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error executing endpoint {Intent}", endpoint.Intent);
            
            return new GenericExecutionResult
            {
                Success = false,
                ErrorMessage = $"Execution error: {ex.Message}",
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    public async Task<ValidationResult> ValidateParametersAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> parameters)
    {
        var result = new ValidationResult { IsValid = true };

        if (endpoint.Schema == null)
        {
            // No schema to validate against, accept all parameters
            result.ValidatedParameters = parameters;
            return result;
        }

        var validatedParams = new Dictionary<string, object>();

        // Check required fields
        foreach (var field in endpoint.Schema.Fields.Where(f => f.IsRequired))
        {
            if (!parameters.ContainsKey(field.Name))
            {
                result.IsValid = false;
                result.MissingRequired.Add(field.Name);
                result.Errors.Add($"Required field '{field.Name}' is missing");
            }
        }

        // Validate and convert each parameter
        foreach (var param in parameters)
        {
            var field = endpoint.Schema.Fields.FirstOrDefault(f => 
                f.Name.Equals(param.Key, StringComparison.OrdinalIgnoreCase) ||
                f.Aliases.Any(a => a.Equals(param.Key, StringComparison.OrdinalIgnoreCase)));

            if (field == null)
            {
                // Unknown field, but we'll keep it
                validatedParams[param.Key] = param.Value;
                continue;
            }

            // Validate against pattern if specified
            if (!string.IsNullOrEmpty(field.ValidationPattern))
            {
                var paramValue = param.Value?.ToString() ?? "";
                if (!Regex.IsMatch(paramValue, field.ValidationPattern))
                {
                    result.IsValid = false;
                    result.Errors.Add(field.ValidationMessage ?? 
                        $"Field '{field.Name}' does not match required pattern");
                    continue;
                }
            }

            // Type conversion
            try
            {
                var convertedValue = ConvertParameterValue(param.Value, field.Type);
                validatedParams[field.Name] = convertedValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed for {Param}: Value='{Value}' ({ValueType}), TargetType='{TargetType}'", 
                    param.Key, param.Value, param.Value?.GetType().Name ?? "null", field.Type);
                
                result.IsValid = false;
                result.Errors.Add($"Cannot convert '{param.Key}' to type '{field.Type}': {ex.Message}");
            }
        }

        result.ValidatedParameters = validatedParams;
        return result;
    }

    private object ConvertParameterValue(object value, string targetType)
    {
        if (value == null) return null!;

        // Handle System.Text.Json.JsonElement
        if (value is JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    value = element.GetString()!;
                    break;
                case JsonValueKind.Number:
                    value = element.GetRawText();
                    break;
                case JsonValueKind.True:
                    value = true;
                    break;
                case JsonValueKind.False:
                    value = false;
                    break;
                default:
                    value = element.ToString();
                    break;
            }
        }

        return targetType.ToLower() switch
        {
            "string" => value.ToString()!,
            "int" or "integer" => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
            "long" => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
            "double" or "number" => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            "decimal" => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
            "bool" or "boolean" => ParseBoolean(value),
            "datetime" or "date" => Convert.ToDateTime(value),
            "guid" => Guid.Parse(value.ToString()!),
            _ => value // Keep as-is for complex types
        };
    }

    private bool ParseBoolean(object value)
    {
        if (value is bool b) return b;
        var str = value.ToString()?.ToLowerInvariant() ?? "";
        
        if (str == "true" || str == "evet" || str == "yes" || str == "y" || str == "1" || str == "onay")
            return true;
        if (str == "false" || str == "hayır" || str == "no" || str == "n" || str == "0" || str == "ret")
            return false;

        return Convert.ToBoolean(value);
    }

    /// <summary>
    /// Convert any value to JToken, handling System.Text.Json.JsonElement properly
    /// </summary>
    private JToken ConvertToJToken(object? value)
    {
        if (value == null)
            return JValue.CreateNull();
            
        // Handle System.Text.Json.JsonElement (from deserializing Dictionary<string, object>)
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            // Parse the raw JSON to Newtonsoft JToken
            return JToken.Parse(jsonElement.GetRawText());
        }
        
        // Handle strings that might be JSON arrays or objects
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            var trimmed = str.Trim();
            if ((trimmed.StartsWith("[") && trimmed.EndsWith("]")) ||
                (trimmed.StartsWith("{") && trimmed.EndsWith("}")))
            {
                try
                {
                    return JToken.Parse(str);
                }
                catch
                {
                    // Not valid JSON, treat as string
                }
            }
        }
        
        return JToken.FromObject(value);
    }

    public async Task<string> BuildRequestBodyAsync(
        EndpointSchema schema,
        Dictionary<string, object> parameters)
    {
        // Build JSON object matching the schema
        var body = new JObject();

        foreach (var field in schema.Fields)
        {
            if (parameters.TryGetValue(field.Name, out var value))
            {
                body[field.Name] = ConvertToJToken(value);
            }
            else if (field.DefaultValue != null)
            {
                body[field.Name] = ConvertToJToken(field.DefaultValue);
            }
        }

        _logger.LogInformation("📦 Building request body - Schema: {TypeName}, IsArray: {IsArray}, Fields: {FieldCount}", 
            schema.TypeName, schema.IsArray, schema.Fields.Count);

        // If schema expects an array, wrap the object in an array
        if (schema.IsArray)
        {
            var array = new JArray { body };
            _logger.LogInformation("📦 Wrapping body in array: {Body}", array.ToString(Formatting.None));
            return array.ToString(Formatting.None);
        }

        _logger.LogInformation("📦 Sending single object: {Body}", body.ToString(Formatting.None));
        return body.ToString(Formatting.None);
    }

    private async Task<HttpRequestMessage> BuildHttpRequestAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> parameters,
        string baseUrl,
        Guid tenantId,
        string? backendToken,
        string? apiKey)
    {
        var url = $"{baseUrl.TrimEnd('/')}{endpoint.Route}";
        
        // Replace route parameters
        foreach (var param in parameters)
        {
            var placeholder = $"{{{param.Key}}}";
            if (url.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                url = url.Replace(placeholder, param.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }

        var request = new HttpRequestMessage(
            new HttpMethod(endpoint.HttpMethod),
            url);

        // Add headers - SayP API Key takes priority over JWT
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        
        // ALWAYS add SayP API Key for authentication (bypasses IdentityServer)
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("X-SayP-Api-Key", apiKey);
            _logger.LogInformation("🔐 Added X-SayP-Api-Key header for authentication");
        }
        else
        {
            // Fallback: Try to get API key from environment
            var envApiKey = Environment.GetEnvironmentVariable("SAYP_API_KEY");
            if (!string.IsNullOrEmpty(envApiKey))
            {
                request.Headers.Add("X-SayP-Api-Key", envApiKey);
                _logger.LogInformation("🔐 Added X-SayP-Api-Key header from environment");
            }
            else
            {
                _logger.LogWarning("⚠️ No SayP API Key available!");
            }
        }
        
        // Also add Bearer token as fallback (for non-SayP-aware endpoints)
        if (!string.IsNullOrEmpty(backendToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", backendToken);
            _logger.LogDebug("Also added Bearer token as fallback");
        }

        // Add body for POST/PUT/PATCH
        if (endpoint.HttpMethod.ToUpper() is "POST" or "PUT" or "PATCH")
        {
            if (endpoint.Schema != null)
            {
                var body = await BuildRequestBodyAsync(endpoint.Schema, parameters);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            else
            {
                // No schema, send parameters as-is
                var body = JsonConvert.SerializeObject(parameters);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
        }
        // Add query parameters for GET/DELETE
        else if (parameters.Any())
        {
            var queryParams = string.Join("&", 
                parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value?.ToString() ?? "")}"));
            
            url = url.Contains('?') ? $"{url}&{queryParams}" : $"{url}?{queryParams}";
            request.RequestUri = new Uri(url);
        }

        return request;
    }

    /// <summary>
    /// ✅ NEW: Execute HTTP request with exponential backoff retry mechanism
    /// </summary>
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        int maxRetries = 3)
    {
        var retryCount = 0;
        var baseDelayMs = 500; // Start with 500ms delay

        while (true)
        {
            try
            {
                // Clone request for retry (original request can only be sent once)
                var requestToSend = retryCount == 0 
                    ? request 
                    : await CloneHttpRequestAsync(request);

                var response = await _httpClient.SendAsync(requestToSend, cancellationToken);

                // Retry on transient errors (5xx, 408, 429)
                if (response.IsSuccessStatusCode || 
                    !IsTransientError(response.StatusCode))
                {
                    return response;
                }

                // Check if we should retry
                if (retryCount >= maxRetries)
                {
                    _logger.LogWarning("Max retries ({MaxRetries}) reached for request. Returning last response.", maxRetries);
                    return response;
                }

                // Calculate delay with exponential backoff
                var delayMs = baseDelayMs * (int)Math.Pow(2, retryCount);
                _logger.LogWarning("Transient error {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})", 
                    response.StatusCode, delayMs, retryCount + 1, maxRetries);

                await Task.Delay(delayMs, cancellationToken);
                retryCount++;
            }
            catch (HttpRequestException ex) when (retryCount < maxRetries)
            {
                // Network errors - retry
                var delayMs = baseDelayMs * (int)Math.Pow(2, retryCount);
                _logger.LogWarning(ex, "Network error, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})", 
                    delayMs, retryCount + 1, maxRetries);

                await Task.Delay(delayMs, cancellationToken);
                retryCount++;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && retryCount < maxRetries)
            {
                // Timeout - retry
                var delayMs = baseDelayMs * (int)Math.Pow(2, retryCount);
                _logger.LogWarning("Request timeout, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})", 
                    delayMs, retryCount + 1, maxRetries);

                await Task.Delay(delayMs, cancellationToken);
                retryCount++;
            }
        }
    }

    /// <summary>
    /// Check if HTTP status code indicates a transient error
    /// </summary>
    private bool IsTransientError(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.RequestTimeout || // 408
               statusCode == System.Net.HttpStatusCode.TooManyRequests || // 429
               (int)statusCode >= 500; // 5xx server errors
    }

    /// <summary>
    /// Clone HTTP request for retry
    /// </summary>
    private async Task<HttpRequestMessage> CloneHttpRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // Copy content headers
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
