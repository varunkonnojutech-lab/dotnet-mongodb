namespace MongoDemo.Services;

/// <summary>
/// Idempotency-key rules, shared by the order endpoint and the unique index
/// on <c>orders.idempotencyKey</c>. The two must agree exactly: if the lock
/// key and the stored key normalize differently, the same logical retry can
/// slip past the uniqueness check and charge twice.
/// </summary>
public static class Idempotency
{
    /// <summary>Trim + lowercase. "  ORDER-1 " and "order-1" are one key.</summary>
    public static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim().ToLowerInvariant();
    }
}
