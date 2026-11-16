using MongoDemo.Services;

namespace MongoDemo.Tests;

public sealed class IdempotencyTests
{
    [Fact]
    public void NormalizeKey_TrimsAndLowercases()
    {
        Assert.Equal("order-1", Idempotency.NormalizeKey("  ORDER-1 "));
    }

    [Fact]
    public void NormalizeKey_IsDeterministic()
    {
        Assert.Equal(Idempotency.NormalizeKey("order-1"), Idempotency.NormalizeKey("order-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeKey_RejectsBlankKeys(string? key)
    {
        // ThrowsAny, not Throws: ThrowIfNullOrWhiteSpace throws the
        // ArgumentNullException subclass for null, plain ArgumentException
        // for empty/whitespace — both are argument errors.
        Assert.ThrowsAny<ArgumentException>(() => Idempotency.NormalizeKey(key!));
    }
}
