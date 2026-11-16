using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDemo.Models;

/// <summary>
/// An order for one product. <see cref="IdempotencyKey"/> is unique in the
/// collection: a retried request with the same key returns the original order
/// instead of charging twice. The key is normalized (trimmed, lower-cased) by
/// <see cref="Services.Idempotency"/> before it ever reaches MongoDB.
/// </summary>
public sealed class Order
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Client-supplied idempotency key, normalized. Unique index.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// The ordered product's id. Stored as an ObjectId (not a string) so the
    /// analytics <c>$lookup</c> joins match — a string key would silently
    /// join nothing. Exposed to C# as the 24-hex string.
    /// </summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProductId { get; set; } = string.Empty;

    public string ProductSku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; set; }

    public string Status { get; set; } = "Created";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
