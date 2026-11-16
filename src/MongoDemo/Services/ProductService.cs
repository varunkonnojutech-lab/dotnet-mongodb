using MongoDB.Bson;
using MongoDB.Driver;
using MongoDemo.Models;

namespace MongoDemo.Services;

/// <summary>
/// Product reads and writes through the official driver. Every query shape in
/// here has a unit test that renders the filter to BSON, so the query logic
/// is proven without a server; the integration tests run the same methods
/// against a real MongoDB.
/// </summary>
public sealed class ProductService
{
    private readonly MongoTopology _mongo;

    public ProductService(MongoTopology mongo) => _mongo = mongo;

    /// <summary>
    /// Builds the combined filter for the catalog search. Pure function of
    /// its inputs — this is what the unit tests render to BSON.
    /// <list type="bullet">
    /// <item><term>search</term><description>$text search, ranked by text score.</description></item>
    /// <item><term>tag</term><description>array containment: <c>Tags</c> contains the tag.</description></item>
    /// <item><term>specKey + specValue</term><description>dot-notation match inside the schemaless <c>Specs</c> document.</description></item>
    /// </list>
    /// </summary>
    public static FilterDefinition<Product> BuildFilter(string? search, string? tag, string? specKey, string? specValue)
    {
        var filters = new List<FilterDefinition<Product>>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add(Builders<Product>.Filter.Text(search));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            filters.Add(Builders<Product>.Filter.AnyEq(p => p.Tags, tag.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(specKey) && specValue is not null)
        {
            // "Specs.color" == "black" — dot notation reaches into the
            // embedded document without loading it.
            filters.Add(Builders<Product>.Filter.Eq($"Specs.{specKey.Trim()}", specValue));
        }

        return filters.Count == 0
            ? Builders<Product>.Filter.Empty
            : Builders<Product>.Filter.And(filters);
    }

    public async Task<List<Product>> SearchAsync(
        string? search, string? tag, string? specKey, string? specValue,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var filter = BuildFilter(search, tag, specKey, specValue);
        var find = _mongo.Products.Find(filter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // $text results come back in text-score order, most relevant first.
            find = find.Sort(Builders<Product>.Sort.MetaTextScore("Score"));
        }
        else
        {
            find = find.SortBy(p => p.Name);
        }

        return await find
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _mongo.Products.Find(p => p.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default) =>
        await _mongo.Products.Find(p => p.Sku == sku).FirstOrDefaultAsync(ct);

    public async Task CreateAsync(Product product, CancellationToken ct = default) =>
        await _mongo.Products.InsertOneAsync(product, cancellationToken: ct);

    /// <summary>Reads one schemaless value without loading the whole document.</summary>
    public async Task<string?> GetSpecAsync(string id, string key, CancellationToken ct = default)
    {
        var projection = Builders<Product>.Projection.Include($"Specs.{key}");
        var product = await _mongo.Products
            .Find(p => p.Id == id)
            .Project<BsonDocument>(projection)
            .FirstOrDefaultAsync(ct);

        return product?.GetValue("Specs", null)?.AsBsonDocument?.GetValue(key, null)?.AsString;
    }

    public async Task<long> CountAsync(CancellationToken ct = default) =>
        await _mongo.Products.CountDocumentsAsync(FilterDefinition<Product>.Empty, cancellationToken: ct);
}
