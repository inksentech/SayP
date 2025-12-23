using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace SayP.Application.Services;

/// <summary>
/// Validates WhatsApp webhook signatures for security
/// </summary>
public class WhatsAppSignatureValidator
{
    private readonly string _appSecret;
    private readonly ILogger<WhatsAppSignatureValidator> _logger;

    public WhatsAppSignatureValidator(
        IConfiguration configuration,
        ILogger<WhatsAppSignatureValidator> logger)
    {
        _appSecret = configuration["WHATSAPP_APP_SECRET"] 
            ?? Environment.GetEnvironmentVariable("WHATSAPP_APP_SECRET")
            ?? throw new InvalidOperationException("WHATSAPP_APP_SECRET is not configured");
        
        _logger = logger;
    }

    /// <summary>
    /// Validate webhook signature
    /// </summary>
    public bool ValidateSignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Signature is missing from webhook request");
            return false;
        }

        try
        {
            // WhatsApp signature format: sha256=<hash>
            if (!signature.StartsWith("sha256="))
            {
                _logger.LogWarning("Invalid signature format: {Signature}", signature);
                return false;
            }

            var expectedSignature = signature.Substring(7); // Remove "sha256=" prefix
            var computedSignature = ComputeSignature(payload);

            var isValid = SecureCompare(expectedSignature, computedSignature);

            if (!isValid)
            {
                _logger.LogWarning("Signature validation failed. Expected: {Expected}, Computed: {Computed}",
                    expectedSignature, computedSignature);
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature");
            return false;
        }
    }

    /// <summary>
    /// Compute HMAC-SHA256 signature
    /// </summary>
    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks
    /// </summary>
    private bool SecureCompare(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}
