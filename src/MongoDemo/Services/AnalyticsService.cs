using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDemo.Data;
using MongoDemo.Models;

namespace MongoDemo.Services;

/// <summary>
/// Aggregation pipelines: the work MongoDB is good at, done server-side.
/// Each pipeline is a static array of <see cref="BsonDocument"/> stages, so
/// the unit tests can assert on the exact stage shapes without a server, and
/// the live methods run those same stages. One subtlety the stages encode:
/// <see cref="Order.ProductId"/> is stored as an ObjectId (see its
/// <c>[BsonRepresentation]</c>), so the <c>$lookup</c> joins match — a plain
/// string key would silently join nothing.
/// </summary>
public sealed class AnalyticsService
{
    private readonly MongoTopology _mongo;
    private readonly MongoDbContext _ef;

    public AnalyticsService(MongoTopology mongo, MongoDbContext ef)
    {
        _mongo = mongo;
        _ef = ef;
    }

    public sealed record RevenueByCategory(string Category, decimal Revenue, int Orders);
    public sealed record TopProduct(string Sku, string Name, int UnitsSold, decimal Revenue);
    public sealed record CategoryRating(string Category, double AverageRating, int Products);

    /// <summary>Revenue per category: orders $lookup products, then $group.</summary>
    public static BsonDocument[] RevenueByCategoryStages() =>
    [
        new("$lookup", new BsonDocument
        {
            { "from", "products" },
            { "localField", "ProductId" },
            { "foreignField", "_id" },
            { "as", "ProductDoc" },
        }),
        new("$unwind", "$ProductDoc"),
        new("$group", new BsonDocument
        {
            { "_id", "$ProductDoc.Category" },
            { "Revenue", new BsonDocument("$sum", "$Total") },
            { "Orders", new BsonDocument("$sum", 1) },
        }),
        new("$sort", new BsonDocument("Revenue", -1)),
    ];

    /// <summary>Top products by units sold, with names joined in.</summary>
    public static BsonDocument[] TopProductsStages(int limit = 5) =>
    [
        new("$group", new BsonDocument
        {
            { "_id", "$ProductId" },
            { "UnitsSold", new BsonDocument("$sum", "$Quantity") },
            { "Revenue", new BsonDocument("$sum", "$Total") },
        }),
        new("$sort", new BsonDocument("UnitsSold", -1)),
        new("$limit", limit),
        new("$lookup", new BsonDocument
        {
            { "from", "products" },
            { "localField", "_id" },
            { "foreignField", "_id" },
            { "as", "ProductDoc" },
        }),
        new("$unwind", "$ProductDoc"),
        new("$project", new BsonDocument
        {
            { "_id", 0 },
            { "Sku", "$ProductDoc.Sku" },
            { "Name", "$ProductDoc.Name" },
            { "UnitsSold", 1 },
            { "Revenue", 1 },
        }),
    ];

    /// <summary>Average rating per category, straight off the products collection.</summary>
    public static BsonDocument[] AverageRatingStages() =>
    [
        new("$group", new BsonDocument
        {
            { "_id", "$Category" },
            { "AverageRating", new BsonDocument("$avg", "$Rating") },
            { "Products", new BsonDocument("$sum", 1) },
        }),
        new("$sort", new BsonDocument("AverageRating", -1)),
    ];

    /// <summary>Runs BSON stages against a collection and returns raw documents.</summary>
    private static async Task<List<BsonDocument>> RunPipelineAsync<T>(
        IMongoCollection<T> collection, BsonDocument[] stages, CancellationToken ct)
    {
        var fluent = collection.Aggregate().AppendStage<BsonDocument>(stages[0]);
        foreach (var stage in stages.Skip(1))
        {
            fluent = fluent.AppendStage<BsonDocument>(stage);
        }
        return await fluent.ToListAsync(ct);
    }

    public async Task<List<RevenueByCategory>> RevenueByCategoryAsync(CancellationToken ct = default)
    {
        var docs = await RunPipelineAsync(_mongo.Orders, RevenueByCategoryStages(), ct);
        return docs.Select(d => new RevenueByCategory(
            d["_id"].AsString, d["Revenue"].ToDecimal(), d["Orders"].AsInt32)).ToList();
    }

    public async Task<List<TopProduct>> TopProductsAsync(int limit = 5, CancellationToken ct = default)
    {
        var docs = await RunPipelineAsync(_mongo.Orders, TopProductsStages(limit), ct);
        return docs.Select(d => new TopProduct(
            d["Sku"].AsString, d["Name"].AsString, d["UnitsSold"].AsInt32, d["Revenue"].ToDecimal())).ToList();
    }

    public async Task<List<CategoryRating>> AverageRatingByCategoryAsync(CancellationToken ct = default)
    {
        var docs = await RunPipelineAsync(_mongo.Products, AverageRatingStages(), ct);
        return docs.Select(d => new CategoryRating(
            d["_id"].AsString, d["AverageRating"].ToDouble(), d["Products"].AsInt32)).ToList();
    }

    /// <summary>
    /// Same question through both APIs: the driver and the EF Core MongoDB
    /// provider, timed. The query is "products in a category, cheapest
    /// first, take 10" — the provider translates the LINQ; the driver runs
    /// it directly. Row counts must match; the timings are the interesting bit.
    /// </summary>
    public async Task<(int DriverRows, long DriverMs, int EfRows, long EfMs)> CompareAsync(
        string category, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var driverRows = await _mongo.Products
            .Find(p => p.Category == category)
            .SortBy(p => p.Price)
            .Limit(10)
            .ToListAsync(ct);
        var driverMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var efRows = await _ef.Products
            .Where(p => p.Category == category)
            .OrderBy(p => p.Price)
            .Take(10)
            .ToListAsync(ct);
        var efMs = sw.ElapsedMilliseconds;

        return (driverRows.Count, driverMs, efRows.Count, efMs);
    }
}
