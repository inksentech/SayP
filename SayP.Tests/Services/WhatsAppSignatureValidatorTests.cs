using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SayP.Application.Services;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SayP.Tests.Services;

public class WhatsAppSignatureValidatorTests
{
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<WhatsAppSignatureValidator>> _loggerMock;
    private const string TestAppSecret = "test_secret_key_12345";

    public WhatsAppSignatureValidatorTests()
    {
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<WhatsAppSignatureValidator>>();
        
        _configMock.Setup(c => c["WHATSAPP_APP_SECRET"]).Returns(TestAppSecret);
    }

    [Fact]
    public void Constructor_MissingAppSecret_ShouldThrowException()
    {
        // Arrange
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["WHATSAPP_APP_SECRET"]).Returns((string?)null);

        // Act & Assert
        Action act = () => new WhatsAppSignatureValidator(configMock.Object, _loggerMock.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WHATSAPP_APP_SECRET*");
    }

    [Fact]
    public void ValidateSignature_ValidSignature_ShouldReturnTrue()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "{\"test\":\"data\"}";
        var signature = GenerateValidSignature(payload, TestAppSecret);

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSignature_InvalidSignature_ShouldReturnFalse()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "{\"test\":\"data\"}";
        var signature = "sha256=invalid_signature_12345";

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_MissingSignature_ShouldReturnFalse()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "{\"test\":\"data\"}";

        // Act
        var result = validator.ValidateSignature(payload, string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_WrongFormat_ShouldReturnFalse()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "{\"test\":\"data\"}";
        var signature = "md5=somehash"; // Wrong format, should be sha256=

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_ModifiedPayload_ShouldReturnFalse()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var originalPayload = "{\"test\":\"data\"}";
        var modifiedPayload = "{\"test\":\"modified\"}";
        var signature = GenerateValidSignature(originalPayload, TestAppSecret);

        // Act
        var result = validator.ValidateSignature(modifiedPayload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_DifferentSecret_ShouldReturnFalse()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "{\"test\":\"data\"}";
        var signature = GenerateValidSignature(payload, "different_secret");

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_EmptyPayload_ShouldHandleGracefully()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "";
        var signature = GenerateValidSignature(payload, TestAppSecret);

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSignature_LargePayload_ShouldValidateCorrectly()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = new string('x', 10000); // 10KB payload
        var signature = GenerateValidSignature(payload, TestAppSecret);

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSignature_SpecialCharacters_ShouldValidateCorrectly()
    {
        // Arrange
        var validator = new WhatsAppSignatureValidator(_configMock.Object, _loggerMock.Object);
        var payload = "{\"message\":\"Hello 👋 Dünya! @#$%^&*()\"}";
        var signature = GenerateValidSignature(payload, TestAppSecret);

        // Act
        var result = validator.ValidateSignature(payload, signature);

        // Assert
        result.Should().BeTrue();
    }

    private string GenerateValidSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}
