using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SayP.Application.Services;
using SayP.Domain.Enums;
using Xunit;

namespace SayP.Tests.Services;

public class EntityExtractorTests
{
    private readonly EntityExtractor _extractor;
    private readonly Mock<ILogger<EntityExtractor>> _loggerMock;

    public EntityExtractorTests()
    {
        _loggerMock = new Mock<ILogger<EntityExtractor>>();
        _extractor = new EntityExtractor(_loggerMock.Object);
    }

    [Theory]
    [InlineData("Laptop ekle, 15000 TL", 15000)]
    [InlineData("Fiyatı 25.000 TL", 25000)]
    [InlineData("Tutar: 1500 TL", 1500)]
    [InlineData("Para 999 lira", 999)]
    public void ExtractPrices_ShouldExtractCorrectly(string message, decimal expectedPrice)
    {
        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("price");
        entities["price"].Should().Be(expectedPrice);
    }

    [Theory]
    [InlineData("10 adet", 10)]
    [InlineData("5 tane laptop", 5)]
    [InlineData("Stok: 100", 100)]
    [InlineData("3x mouse", 3)]
    public void ExtractQuantities_ShouldExtractCorrectly(string message, int expectedQuantity)
    {
        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("stockQuantity");
        entities["stockQuantity"].Should().Be(expectedQuantity);
    }

    [Theory]
    [InlineData("KDV 18", 18)]
    [InlineData("Vergi %20", 20)]
    [InlineData("Tax rate: 8%", 8)]
    public void ExtractPercentages_ShouldExtractCorrectly(string message, decimal expectedRate)
    {
        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("taxRate");
        entities["taxRate"].Should().Be(expectedRate);
    }

    [Theory]
    [InlineData("+90 555 123 45 67")]
    [InlineData("Tel: 0555 123 45 67")]
    [InlineData("Telefon: +905551234567")]
    public void ExtractPhoneNumbers_ShouldExtractCorrectly(string message)
    {
        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("phone");
        entities["phone"].Should().NotBeNull();
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("Email: user@domain.co.uk")]
    [InlineData("Mail: admin@test.org")]
    public void ExtractEmails_ShouldExtractCorrectly(string message)
    {
        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("email");
        entities["email"].Should().NotBeNull();
    }

    [Fact]
    public void ExtractProductNames_ShouldExtractQuotedNames()
    {
        // Arrange
        var message = "Ürün adı \"Gaming Laptop\" ekle";

        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("name");
        entities["name"].Should().Be("Gaming Laptop");
    }

    [Fact]
    public void ExtractDates_ShouldExtractRelativeDates()
    {
        // Arrange
        var message = "Bugün için fatura kes";

        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().ContainKey("date");
        var date = (DateTime)entities["date"];
        date.Date.Should().Be(DateTime.Today);
    }

    [Fact]
    public void ExtractPriceRange_ShouldExtractMinMax()
    {
        // Arrange
        var message = "10000-20000 TL arası ürünler";

        // Act
        var entities = _extractor.ExtractEntities(message, CommandType.SearchProducts);

        // Assert
        entities.Should().ContainKey("minPrice");
        entities.Should().ContainKey("maxPrice");
        entities["minPrice"].Should().Be(10000m);
        entities["maxPrice"].Should().Be(20000m);
    }

    [Fact]
    public void NormalizeEntities_ShouldNormalizeTaxRate()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "taxRate", 18m }
        };

        // Act
        var normalized = _extractor.NormalizeEntities(entities);

        // Assert
        normalized["taxRate"].Should().Be(0.18m);
    }

    [Fact]
    public void NormalizeEntities_ShouldNormalizePhone()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "phone", "+90 (555) 123-45-67" }
        };

        // Act
        var normalized = _extractor.NormalizeEntities(entities);

        // Assert
        normalized["phone"].Should().Be("+905551234567");
    }

    [Fact]
    public void NormalizeEntities_ShouldCapitalizeNames()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "gaming laptop" }
        };

        // Act
        var normalized = _extractor.NormalizeEntities(entities);

        // Assert
        normalized["name"].Should().Be("Gaming Laptop");
    }

    [Fact]
    public void ExtractEntities_ComplexMessage_ShouldExtractMultipleEntities()
    {
        // Arrange
        var message = "Laptop ekle, fiyat 15000 TL, KDV 18, stok 50 adet, tel: +90 555 123 45 67";

        // Act
        var entities = _extractor.ExtractEntities(message);

        // Assert
        entities.Should().HaveCountGreaterThan(3);
        entities.Should().ContainKey("price");
        entities.Should().ContainKey("taxRate");
        entities.Should().ContainKey("stockQuantity");
        entities.Should().ContainKey("phone");
    }
}
