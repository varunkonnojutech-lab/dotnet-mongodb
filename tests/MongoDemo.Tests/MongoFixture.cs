using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.EntityFrameworkCore;
using MongoDemo.Data;
using MongoDemo.Models;
using MongoDemo.Services;

namespace MongoDemo.Tests;

/// <summary>
/// Integration tests against a real MongoDB. They run in CI, where the
/// workflow starts a single-node replica set in Docker; locally they need
/// one too (<c>docker compose up -d mongo mongo-init</c>, or
/// <c>mongod --replSet rs0</c> — the fixture initiates the set itself).
/// Transactions and change streams are replica-set behaviour and cannot be
/// faked against a standalone server, so when no replica set is available
/// the fixture fails with a clear message instead of silently skipping.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    public const string TestDatabase = "mongodemotest";

    public MongoTopology Mongo { get; }

    public ProductService Products { get; }

    public OrderService Orders { get; }

    public AnalyticsService Analytics { get; }

    public ChangeEventStore ChangeStore { get; } = new();

    public string ConnectionString { get; }

    public MongoFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable("MONGO_TEST_CONNECTION_STRING")
            ?? "mongodb://localhost:27017/mongodemotest?replicaSet=rs0";
        Mongo = new MongoTopology(ConnectionString, TestDatabase);
        var efOptions = new DbContextOptionsBuilder<MongoDbContext>()
            .UseMongoDB(ConnectionString, TestDatabase)
            .Options;
        Products = new ProductService(Mongo);
        Orders = new OrderService(Mongo);
        Analytics = new AnalyticsService(Mongo, new MongoDbContext(efOptions));
    }

    public async Task InitializeAsync()
    {
        await EnsureReplicaSetAsync();
        if (!await Mongo.InitializeAsync(NullLogger.Instance))
        {
            throw new InvalidOperationException(
                "Could not reach MongoDB. Start one with `docker compose up -d mongo mongo-init` " +
                "or set MONGO_TEST_CONNECTION_STRING.");
        }

        // Clean slate, then the same seed set as the API.
        await Mongo.Products.DeleteManyAsync(FilterDefinition<Product>.Empty);
        await Mongo.Orders.DeleteManyAsync(FilterDefinition<Order>.Empty);
        await Mongo.ChangeEvents.DeleteManyAsync(FilterDefinition<ChangeEvent>.Empty);
        await SeedData.SeedAsync(Mongo);
        await SeedOrdersAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The workflow and docker-compose both initiate the replica set, but a
    /// hand-started <c>mongod --replSet rs0</c> needs initiating too — the
    /// fixture does it, then waits for PRIMARY. A standalone server (no
    /// --replSet) fails here with instructions, not a silent skip.
    /// <para>
    /// The initiate runs over a <em>direct</em> connection: with
    /// <c>replicaSet=rs0</c> in the connection string the driver waits for a
    /// PRIMARY before running anything, and an uninitiated node is a ghost
    /// with no primary — every command would time out in server selection.
    /// </para>
    /// </summary>
    private async Task EnsureReplicaSetAsync()
    {
        var url = new MongoUrl(ConnectionString);
        var server = url.Server;
        var directSettings = MongoClientSettings.FromConnectionString($"mongodb://{server.Host}:{server.Port}");
        directSettings.DirectConnection = true;
        directSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var admin = new MongoClient(directSettings).GetDatabase("admin");
        var memberHost = $"{server.Host}:{server.Port}";
        var timeout = TimeSpan.FromSeconds(60);
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow - started < timeout)
        {
            try
            {
                var hello = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
                if (hello.Contains("setName"))
                {
                    if (hello.GetValue("isWritablePrimary", false).ToBoolean())
                    {
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    continue;
                }

                await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate",
                    new BsonDocument
                    {
                        { "_id", "rs0" },
                        { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", memberHost } } } },
                    }));
            }
            catch (MongoCommandException ex) when (ex.Message.Contains("already initiated", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            catch (MongoCommandException ex) when (ex.Message.Contains("replSet", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "MongoDB is running as a standalone server, but transactions and change streams need a " +
                    "replica set. Start it with `mongod --replSet rs0` (or `docker compose up -d mongo mongo-init`) " +
                    "and re-run the tests.", ex);
            }
            catch (Exception) when (DateTime.UtcNow - started < timeout)
            {
                // Not up yet — retry until the timeout.
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException(
            "MongoDB did not become a PRIMARY replica-set member within 60s. " +
            "Start one with `docker compose up -d mongo mongo-init` or set MONGO_TEST_CONNECTION_STRING.");
    }

    private async Task SeedOrdersAsync()
    {
        var headphones = await Products.GetBySkuAsync("HP-300");
        var keyboard = await Products.GetBySkuAsync("KB-120");
        var monitor = await Products.GetBySkuAsync("MN-270");
        ArgumentNullException.ThrowIfNull(headphones);
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(monitor);

        await Mongo.Orders.InsertManyAsync(new[]
        {
            new Order { IdempotencyKey = "seed-order-1", ProductId = headphones.Id, ProductSku = headphones.Sku, Quantity = 2, Total = headphones.Price * 2 },
            new Order { IdempotencyKey = "seed-order-2", ProductId = keyboard.Id, ProductSku = keyboard.Sku, Quantity = 1, Total = keyboard.Price },
            new Order { IdempotencyKey = "seed-order-3", ProductId = monitor.Id, ProductSku = monitor.Sku, Quantity = 1, Total = monitor.Price },
        });
    }

    /// <summary>Replaces all orders with exactly the given set (test-local setup).</summary>
    public async Task ResetOrdersAsync(IEnumerable<Order> orders)
    {
        await Mongo.Orders.DeleteManyAsync(FilterDefinition<Order>.Empty);
        await Mongo.Orders.InsertManyAsync(orders);
    }

    public Task<Product?> GetProductBySkuAsync(string sku) => Products.GetBySkuAsync(sku);
}
