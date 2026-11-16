using MongoDB.Bson;
using MongoDB.Driver;
using MongoDemo.Models;

namespace MongoDemo.Services;

/// <summary>
/// Owns the connection and the database shape: which collections exist, which
/// indexes they carry, and whether the deployment is a replica set.
/// <para>
/// Two features in this repo — multi-document transactions and change streams —
/// only work on a replica set. The demo runs a single-node replica set in
/// Docker Compose and in CI, but a plain standalone <c>mongod</c> still works:
/// <see cref="IsReplicaSet"/> is false, the order endpoint falls back to
/// sequential writes, and the change-stream service stays idle. The topology
/// is detected, never assumed.
/// </para>
/// </summary>
public sealed class MongoTopology
{
    public const string DefaultDatabaseName = "mongodemo";

    public string DatabaseName { get; }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Product> Products => Database.GetCollection<Product>("products");

    public IMongoCollection<Order> Orders => Database.GetCollection<Order>("orders");

    public IMongoCollection<ChangeEvent> ChangeEvents => Database.GetCollection<ChangeEvent>("change_events");

    /// <summary>True when the server reports a replica set name.</summary>
    public bool IsReplicaSet { get; private set; }

    public MongoTopology(string connectionString, string? databaseName = null)
    {
        DatabaseName = databaseName ?? DefaultDatabaseName;
        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(DatabaseName);
    }

    /// <summary>
    /// Detects the topology and creates the indexes. Retries briefly so
    /// <c>docker compose up</c> works while MongoDB is still starting.
    /// Returns false only when MongoDB never became reachable.
    /// </summary>
    public async Task<bool> InitializeAsync(ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "MongoDB not reachable yet (attempt {Attempt}/10) — retrying in 2s.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MongoDB never became reachable — the API will start without a database.");
                return false;
            }
        }

        try
        {
            var hello = await Database.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct);
            IsReplicaSet = hello.Contains("setName");
            logger.LogInformation("MongoDB topology: {Topology}.",
                IsReplicaSet ? $"replica set '{hello["setName"]}'" : "standalone (no transactions / change streams)");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not detect MongoDB topology — assuming standalone.");
            IsReplicaSet = false;
        }

        await EnsureIndexesAsync();
        return true;
    }

    /// <summary>All indexes the demo relies on, created idempotently.</summary>
    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        // $text search across name + description (GET /products?search=).
        await Products.Indexes.CreateOneAsync(
            new CreateIndexModel<Product>(
                Builders<Product>.IndexKeys.Text(p => p.Name).Text(p => p.Description)),
            cancellationToken: ct);

        // Array containment (GET /products?tag=) and category filters.
        await Products.Indexes.CreateOneAsync(
            new CreateIndexModel<Product>(Builders<Product>.IndexKeys.Ascending(p => p.Tags)),
            cancellationToken: ct);
        await Products.Indexes.CreateOneAsync(
            new CreateIndexModel<Product>(Builders<Product>.IndexKeys.Ascending(p => p.Category)),
            cancellationToken: ct);
        await Products.Indexes.CreateOneAsync(
            new CreateIndexModel<Product>(
                Builders<Product>.IndexKeys.Ascending(p => p.Sku),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        // The idempotency backstop: a retried order with the same key
        // violates this index instead of creating a second charge.
        await Orders.Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(
                Builders<Order>.IndexKeys.Ascending(o => o.IdempotencyKey),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        // Change history is a rolling 7-day window, not a forever table.
        await ChangeEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<ChangeEvent>(
                Builders<ChangeEvent>.IndexKeys.Ascending(e => e.SeenAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) }),
            cancellationToken: ct);
    }
}
