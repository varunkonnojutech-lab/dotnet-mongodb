using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDemo.Models;
using MongoDemo.Services;

namespace MongoDemo.Tests;

public sealed class MongoIntegrationTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fixture;

    public MongoIntegrationTests(MongoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TextSearch_FindsProductByDescriptionWords()
    {
        var results = await _fixture.Products.SearchAsync("noise-cancelling wireless", null, null, null);

        Assert.Contains(results, p => p.Sku == "HP-300");
    }

    [Fact]
    public async Task TextSearch_RanksBestMatchFirst()
    {
        // "wireless headphones" appears in the name AND the description of
        // HP-300, so its text score beats every other wireless product.
        var results = await _fixture.Products.SearchAsync("wireless headphones", null, null, null);

        Assert.NotEmpty(results);
        Assert.Equal("HP-300", results[0].Sku);
    }

    [Fact]
    public async Task TagFilter_FindsProductsWithTag()
    {
        var results = await _fixture.Products.SearchAsync(null, "usb-c", null, null);

        Assert.Contains(results, p => p.Sku == "KB-120");
        Assert.Contains(results, p => p.Sku == "MN-270");
        Assert.DoesNotContain(results, p => p.Sku == "HP-300");
    }

    [Fact]
    public async Task SpecFilter_FindsProductBySchemalessAttribute()
    {
        var results = await _fixture.Products.SearchAsync(null, null, "color", "black");

        Assert.Contains(results, p => p.Sku == "HP-300");
        Assert.Contains(results, p => p.Sku == "MS-050");
        Assert.DoesNotContain(results, p => p.Sku == "KB-120");
    }

    [Fact]
    public async Task GetSpec_ReturnsSingleValueWithoutLoadingDocument()
    {
        var mouse = await _fixture.GetProductBySkuAsync("MS-050");
        ArgumentNullException.ThrowIfNull(mouse);

        Assert.Equal("4000", await _fixture.Products.GetSpecAsync(mouse.Id, "dpi"));
        Assert.Null(await _fixture.Products.GetSpecAsync(mouse.Id, "no-such-key"));
    }

    [Fact]
    public async Task IdempotentOrder_SecondCallReturnsOriginal_NoSecondCharge()
    {
        var headphones = await _fixture.GetProductBySkuAsync("HP-300");
        ArgumentNullException.ThrowIfNull(headphones);
        var stockBefore = headphones.Stock;
        const string key = "test-idempotent-1";

        var first = await _fixture.Orders.CreateOrderAsync(key, headphones.Id, 1);
        var second = await _fixture.Orders.CreateOrderAsync(key, headphones.Id, 1);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Order.Id, second.Order.Id);

        var orders = await _fixture.Mongo.Orders
            .Find(o => o.IdempotencyKey == Idempotency.NormalizeKey(key)).ToListAsync();
        Assert.Single(orders);

        var after = await _fixture.GetProductBySkuAsync("HP-300");
        Assert.Equal(stockBefore - 1, after!.Stock);
    }

    [Fact]
    public async Task ConcurrentOrders_SameKey_CreateExactlyOne()
    {
        var stand = await _fixture.GetProductBySkuAsync("WB-400");
        ArgumentNullException.ThrowIfNull(stand);
        var stockBefore = stand.Stock;
        const string key = "test-concurrent-1";

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => _fixture.Orders.CreateOrderAsync(key, stand.Id, 1));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Single(results.Select(r => r!.Order.Id).Distinct());
        Assert.Single(results.Where(r => r!.WasCreated));

        var orders = await _fixture.Mongo.Orders
            .Find(o => o.IdempotencyKey == Idempotency.NormalizeKey(key)).ToListAsync();
        Assert.Single(orders);

        var after = await _fixture.GetProductBySkuAsync("WB-400");
        Assert.Equal(stockBefore - 1, after!.Stock);
    }

    [Fact]
    public async Task Order_InsufficientStock_ReturnsNull()
    {
        var monitor = await _fixture.GetProductBySkuAsync("MN-270");
        ArgumentNullException.ThrowIfNull(monitor);

        var result = await _fixture.Orders.CreateOrderAsync("test-no-stock-1", monitor.Id, monitor.Stock + 10);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevenueByCategory_GroupsOrdersCorrectly()
    {
        var headphones = await _fixture.GetProductBySkuAsync("HP-300");
        var keyboard = await _fixture.GetProductBySkuAsync("KB-120");
        ArgumentNullException.ThrowIfNull(headphones);
        ArgumentNullException.ThrowIfNull(keyboard);
        await _fixture.ResetOrdersAsync(new[]
        {
            new Order { IdempotencyKey = "rev-1", ProductId = headphones.Id, ProductSku = headphones.Sku, Quantity = 2, Total = headphones.Price * 2 },
            new Order { IdempotencyKey = "rev-2", ProductId = keyboard.Id, ProductSku = keyboard.Sku, Quantity = 1, Total = keyboard.Price },
        });

        var revenue = await _fixture.Analytics.RevenueByCategoryAsync();

        Assert.Equal(2, revenue.Count);
        Assert.Equal("Audio", revenue[0].Category);
        Assert.Equal(399.98m, revenue[0].Revenue);
        Assert.Equal(1, revenue[0].Orders);
        Assert.Equal("Accessories", revenue[1].Category);
        Assert.Equal(129.00m, revenue[1].Revenue);
    }

    [Fact]
    public async Task TopProducts_OrdersByUnitsSold()
    {
        var headphones = await _fixture.GetProductBySkuAsync("HP-300");
        var keyboard = await _fixture.GetProductBySkuAsync("KB-120");
        var mouse = await _fixture.GetProductBySkuAsync("MS-050");
        ArgumentNullException.ThrowIfNull(headphones);
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(mouse);
        await _fixture.ResetOrdersAsync(new[]
        {
            new Order { IdempotencyKey = "top-1", ProductId = mouse.Id, ProductSku = mouse.Sku, Quantity = 3, Total = mouse.Price * 3 },
            new Order { IdempotencyKey = "top-2", ProductId = headphones.Id, ProductSku = headphones.Sku, Quantity = 2, Total = headphones.Price * 2 },
            new Order { IdempotencyKey = "top-3", ProductId = keyboard.Id, ProductSku = keyboard.Sku, Quantity = 1, Total = keyboard.Price },
        });

        var top = await _fixture.Analytics.TopProductsAsync(2);

        Assert.Equal(2, top.Count);
        Assert.Equal("MS-050", top[0].Sku);
        Assert.Equal(3, top[0].UnitsSold);
        Assert.Equal("HP-300", top[1].Sku);
    }

    [Fact]
    public async Task AverageRatingByCategory_AveragesCorrectly()
    {
        // Audio has exactly the two seeded products: 4.7 and 4.3.
        var ratings = await _fixture.Analytics.AverageRatingByCategoryAsync();

        var audio = Assert.Single(ratings, r => r.Category == "Audio");
        Assert.Equal(4.5, audio.AverageRating, precision: 5);
        Assert.Equal(2, audio.Products);
    }

    [Fact]
    public async Task Compare_DriverAndEfCore_ReturnSameRows()
    {
        var (driverRows, _, efRows, _) = await _fixture.Analytics.CompareAsync("Audio");

        Assert.Equal(2, driverRows);
        Assert.Equal(driverRows, efRows);
    }

    [Fact]
    public async Task ChangeStream_InsertAppearsInStore()
    {
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => { });
        var service = new ChangeStreamService(
            _fixture.Mongo, _fixture.ChangeStore,
            loggerFactory.CreateLogger<ChangeStreamService>());
        await service.StartAsync(CancellationToken.None);
        try
        {
            // Give the watch cursor a moment to open before inserting.
            await Task.Delay(TimeSpan.FromSeconds(2));

            var product = new Product
            {
                Sku = "CS-TEST-1", Name = "Change Stream Test Product",
                Description = "Inserted only to prove the change stream sees it.",
                Category = "Test", Price = 1.00m, Stock = 1, Rating = 5.0,
            };
            await _fixture.Products.CreateAsync(product);

            ChangeEvent? seen = null;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                seen = _fixture.ChangeStore.Recent().FirstOrDefault(e => e.ProductSku == "CS-TEST-1");
                if (seen is not null)
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            Assert.NotNull(seen);
            Assert.Equal("insert", seen.OperationType);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Indexes_Exist()
    {
        var productIndexes = await _fixture.Mongo.Products.Indexes.List().ToListAsync();
        var orderIndexes = await _fixture.Mongo.Orders.Indexes.List().ToListAsync();
        var eventIndexes = await _fixture.Mongo.ChangeEvents.Indexes.List().ToListAsync();

        // $text index on Name + Description. MongoDB stores a text index
        // under the internal _fts key, with the covered fields in weights.
        Assert.Contains(productIndexes, i =>
            i["key"].AsBsonDocument.GetValue("_fts", null)?.AsString == "text" &&
            i.GetValue("weights", null)?.AsBsonDocument.Contains("Name") == true &&
            i.GetValue("weights", null)?.AsBsonDocument.Contains("Description") == true);
        // Unique SKU.
        Assert.Contains(productIndexes, i =>
            i.GetValue("unique", false).ToBoolean() &&
            i["key"].AsBsonDocument.Contains("Sku"));
        // Unique idempotency key.
        Assert.Contains(orderIndexes, i =>
            i.GetValue("unique", false).ToBoolean() &&
            i["key"].AsBsonDocument.Contains("IdempotencyKey"));
        // 7-day TTL on change events.
        Assert.Contains(eventIndexes, i =>
            i.GetValue("expireAfterSeconds", -1).ToInt32() == 7 * 24 * 60 * 60);
    }
}
