using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDemo.Models;

/// <summary>
/// One product change captured from the change stream, kept for the
/// <c>GET /changes</c> endpoint and the <c>change_events</c> collection.
/// The collection has a TTL index (7 days): change history is a rolling
/// window, not a table that grows forever.
/// </summary>
public sealed class ChangeEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>insert, update, replace or delete.</summary>
    public string OperationType { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string ProductSku { get; set; } = string.Empty;

    public DateTime SeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>The change-stream resume token, so a restart can pick up where it left off.</summary>
    public string? ResumeToken { get; set; }
}
