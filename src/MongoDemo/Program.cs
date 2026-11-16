using Microsoft.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore;
using MongoDemo.Data;
using MongoDemo.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MongoDb")
    ?? throw new InvalidOperationException("ConnectionStrings:MongoDb is not configured.");

// The topology is a singleton: it owns the client, the database handle and
// the replica-set detection every other service reads.
builder.Services.AddSingleton(sp => new MongoTopology(connectionString));
builder.Services.AddDbContext<MongoDbContext>(options => options.UseMongoDB(connectionString, MongoTopology.DefaultDatabaseName));
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddSingleton<ChangeEventStore>();
builder.Services.AddHostedService<ChangeStreamService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// The API owns its database shape: detect the topology, create the indexes
// (with a short retry so `docker compose up` works while MongoDB is still
// starting), then seed.
using (var scope = app.Services.CreateScope())
{
    var mongo = scope.ServiceProvider.GetRequiredService<MongoTopology>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    if (await mongo.InitializeAsync(logger))
    {
        await SeedData.SeedAsync(mongo);
    }
    else
    {
        logger.LogError("Could not reach MongoDB — the API will start without a database.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "MongoDemo",
    description = "MongoDB in .NET 8: official driver, aggregation pipelines, change streams, multi-document transactions, EF Core provider comparison.",
    endpoints = new[]
    {
        "GET /health",
        "GET /products?search=&tag=&specKey=&specValue=",
        "GET /products/{id}",
        "GET /products/{id}/specs/{key}",
        "POST /products",
        "POST /orders (Idempotency-Key header)",
        "GET /analytics/revenue-by-category",
        "GET /analytics/top-products",
        "GET /analytics/average-rating",
        "GET /compare?category=Audio",
        "GET /changes",
    },
}));

app.MapGet("/health", async (MongoTopology mongo) =>
{
    try
    {
        await mongo.Database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
        return Results.Ok(new
        {
            status = "healthy",
            database = mongo.DatabaseName,
            topology = mongo.IsReplicaSet ? "replica-set" : "standalone",
            transactions = mongo.IsReplicaSet ? "available" : "unavailable (needs a replica set)",
            changeStreams = mongo.IsReplicaSet ? "available" : "unavailable (needs a replica set)",
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "unhealthy", error = ex.Message }, statusCode: 503);
    }
});

app.MapGet("/products", async (
    ProductService products,
    string? search, string? tag, string? specKey, string? specValue,
    int page = 1, int pageSize = 20) =>
    Results.Ok(await products.SearchAsync(search, tag, specKey, specValue, page, pageSize)));

app.MapGet("/products/{id}", async (ProductService products, string id) =>
    await products.GetByIdAsync(id) is { } product
        ? Results.Ok(product)
        : Results.NotFound(new { error = $"No product with id '{id}'." }));

app.MapGet("/products/{id}/specs/{key}", async (ProductService products, string id, string key) =>
    await products.GetSpecAsync(id, key) is { } value
        ? Results.Ok(new { key, value })
        : Results.NotFound(new { error = $"No spec '{key}' on product '{id}'." }));

app.MapPost("/products", async (ProductService products, MongoDemo.Models.Product product) =>
{
    await products.CreateAsync(product);
    return Results.Created($"/products/{product.Id}", product);
});

app.MapPost("/orders", async (OrderService orders, HttpRequest request, CreateOrderRequest body) =>
{
    if (!request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest(new { error = "The Idempotency-Key header is required." });
    }

    var result = await orders.CreateOrderAsync(key!, body.ProductId, body.Quantity);
    return result switch
    {
        null => Results.BadRequest(new { error = "Unknown product or not enough stock." }),
        { WasCreated: true } => Results.Created($"/orders/{result.Order.Id}", new
        {
            result.Order,
            transaction = result.UsedTransaction ? "multi-document transaction" : "sequential writes (standalone server)",
        }),
        _ => Results.Ok(new
        {
            result.Order,
            replayed = true,
            note = "Same Idempotency-Key as an earlier order — returning the original, no second charge.",
        }),
    };
});

app.MapGet("/analytics/revenue-by-category", async (AnalyticsService analytics) =>
    Results.Ok(await analytics.RevenueByCategoryAsync()));

app.MapGet("/analytics/top-products", async (AnalyticsService analytics, int limit = 5) =>
    Results.Ok(await analytics.TopProductsAsync(limit)));

app.MapGet("/analytics/average-rating", async (AnalyticsService analytics) =>
    Results.Ok(await analytics.AverageRatingByCategoryAsync()));

app.MapGet("/compare", async (AnalyticsService analytics, string category = "Audio") =>
{
    var (driverRows, driverMs, efRows, efMs) = await analytics.CompareAsync(category);
    return Results.Ok(new
    {
        query = $"products in '{category}', cheapest first, take 10",
        driver = new { rows = driverRows, milliseconds = driverMs },
        efCoreMongoDbProvider = new { rows = efRows, milliseconds = efMs },
        rowCountsMatch = driverRows == efRows,
    });
});

app.MapGet("/changes", (ChangeEventStore store) =>
    Results.Ok(store.Recent()));

app.Run();

public sealed record CreateOrderRequest(string ProductId, int Quantity);
