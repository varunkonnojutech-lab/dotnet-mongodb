using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDemo.Models;

/// <summary>
/// A product in the catalog. MongoDB's document model is the point here:
/// <see cref="Specs"/> is a schemaless bag of attributes (like the JSONB
/// column in <c>dotnet-postgresql</c>), <see cref="Tags"/> is a plain array,
/// and the whole product is one document — no joins to read it.
/// </summary>
public sealed class Product
{
    /// <summary>Stored as ObjectId, exposed as its 24-hex string form.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Schemaless attributes, e.g. { "color": "black", "batteryHours": "40" }.
    /// One product can have keys another one doesn't — that's the document
    /// model earning its keep. Keep values strings so the $text index and
    /// the dot-notation queries stay simple.
    /// </summary>
    public Dictionary<string, string> Specs { get; set; } = new();

    public int Stock { get; set; }

    public double Rating { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
