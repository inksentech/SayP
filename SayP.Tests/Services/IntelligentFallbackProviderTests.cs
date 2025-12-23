using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SayP.Application.Services;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using Xunit;

namespace SayP.Tests.Services;

public class IntelligentFallbackProviderTests
{
    private readonly IntelligentFallbackProvider _provider;
    private readonly Mock<ILogger<IntelligentFallbackProvider>> _loggerMock;
    private readonly Mock<EntityExtractor> _extractorMock;
    private readonly Mock<IAIProvider> _primaryAIMock;
    private readonly Mock<IAIProvider> _secondaryAIMock;

    public IntelligentFallbackProviderTests()
    {
        _loggerMock = new Mock<ILogger<IntelligentFallbackProvider>>();
        var extractorLoggerMock = new Mock<ILogger<EntityExtractor>>();
        _extractorMock = new Mock<EntityExtractor>(extractorLoggerMock.Object);
        
        _primaryAIMock = new Mock<IAIProvider>();
        _secondaryAIMock = new Mock<IAIProvider>();
        
        _provider = new IntelligentFallbackProvider(_loggerMock.Object, _extractorMock.Object);
    }

    [Fact]
    public async Task ExtractWithFallbackAsync_HighConfidence_ShouldUsePrimaryOnly()
    {
        // Arrange
        var message = "Laptop ekle, 15000 TL";
        var highConfidenceResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.9,
            CommandType = "CreateProduct",
            CommandJson = "{}"
        };

        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(highConfidenceResult);

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object,
            _secondaryAIMock.Object);

        // Assert
        result.Confidence.Should().Be(0.9);
        _primaryAIMock.Verify(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()), Times.Once);
        _secondaryAIMock.Verify(s => s.ExtractCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractWithFallbackAsync_LowConfidence_ShouldTrySecondary()
    {
        // Arrange
        var message = "Laptop ekle";
        var lowConfidenceResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.5,
            CommandType = "CreateProduct"
        };

        var highConfidenceResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.8,
            CommandType = "CreateProduct"
        };

        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowConfidenceResult);

        _secondaryAIMock
            .Setup(s => s.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(highConfidenceResult);

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object,
            _secondaryAIMock.Object);

        // Assert
        result.Confidence.Should().Be(0.8);
        _secondaryAIMock.Verify(s => s.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractWithFallbackAsync_VeryLowConfidence_ShouldTryRuleBased()
    {
        // Arrange
        var message = "ürün ekle";
        var veryLowConfidenceResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.2,
            CommandType = "Unknown"
        };

        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(veryLowConfidenceResult);

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object);

        // Assert
        result.Should().NotBeNull();
        result.Confidence.Should().BeGreaterThan(0.2); // Rule-based should improve confidence
    }

    [Fact]
    public async Task ExtractWithFallbackAsync_AllFail_ShouldReturnClarification()
    {
        // Arrange
        var message = "xyz abc";
        var failedResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.1,
            CommandType = "Unknown"
        };

        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResult);

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object);

        // Assert
        result.CommandType.Should().Be("None");
        result.CommandJson.Should().Contain("anlayamadım");
    }

    [Fact]
    public void SuggestCorrections_CommonTypos_ShouldCorrect()
    {
        // Arrange
        var message = "laptp ekl";

        // Act
        var corrected = _provider.SuggestCorrections(message);

        // Assert
        corrected.Should().Contain("laptop");
        corrected.Should().Contain("ekle");
    }

    [Fact]
    public void SuggestCorrections_NoTypos_ShouldReturnOriginal()
    {
        // Arrange
        var message = "laptop ekle";

        // Act
        var corrected = _provider.SuggestCorrections(message);

        // Assert
        corrected.Should().Be(message);
    }

    [Fact]
    public void IsAmbiguous_PronounWithoutContext_ShouldReturnTrue()
    {
        // Arrange
        var message = "onu sil";

        // Act
        var isAmbiguous = _provider.IsAmbiguous(message);

        // Assert
        isAmbiguous.Should().BeTrue();
    }

    [Fact]
    public void IsAmbiguous_ActionWithoutObject_ShouldReturnTrue()
    {
        // Arrange
        var message = "ekle";

        // Act
        var isAmbiguous = _provider.IsAmbiguous(message);

        // Assert
        isAmbiguous.Should().BeTrue();
    }

    [Fact]
    public void IsAmbiguous_ClearMessage_ShouldReturnFalse()
    {
        // Arrange
        var message = "Laptop ekle, 15000 TL";

        // Act
        var isAmbiguous = _provider.IsAmbiguous(message);

        // Assert
        isAmbiguous.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractWithFallbackAsync_PrimaryFails_ShouldHandleGracefully()
    {
        // Arrange
        var message = "test";
        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI Provider Error"));

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object);

        // Assert
        result.Should().NotBeNull();
        // When primary fails, fallback tries rule-based which may succeed
        result.Confidence.Should().BeLessThan(0.7);
    }

    [Theory]
    [InlineData("ürün ekle", "CreateProduct")]
    [InlineData("ürün güncelle", "UpdateProduct")]
    [InlineData("ürün sil", "DeleteProduct")]
    [InlineData("ürün listele", "ListProducts")]
    [InlineData("ürün ara", "SearchProducts")]
    public async Task ExtractWithFallbackAsync_RuleBased_ShouldDetectProductCommands(string message, string expectedCommand)
    {
        // Arrange
        var lowConfidenceResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.1,
            CommandType = "Unknown"
        };

        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowConfidenceResult);

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object);

        // Assert
        result.CommandType.Should().Be(expectedCommand);
    }

    [Theory]
    [InlineData("müşteri ekle", "CreateCustomer")]
    [InlineData("müşteri listele", "ListCustomers")]
    public async Task ExtractWithFallbackAsync_RuleBased_ShouldDetectCustomerCommands(string message, string expectedCommand)
    {
        // Arrange
        var lowConfidenceResult = new AICommandResult
        {
            Success = true,
            Confidence = 0.1,
            CommandType = "Unknown"
        };

        _primaryAIMock
            .Setup(p => p.ExtractCommandAsync(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowConfidenceResult);

        // Act
        var result = await _provider.ExtractWithFallbackAsync(
            message,
            _primaryAIMock.Object);

        // Assert
        result.CommandType.Should().Be(expectedCommand);
    }
}
