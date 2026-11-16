using MongoDB.Driver;
using MongoDemo.Models;

namespace MongoDemo.Services;

/// <summary>
/// Order creation with idempotency. On a replica set the order insert and
/// the stock decrement run in one multi-document transaction — either both
/// land or neither does, so a crash mid-order can't sell stock it never
/// recorded. On a standalone server the same two writes run sequentially
/// (documented fallback: the unique index on the idempotency key is still
/// the backstop against double charges).
/// </summary>
public sealed class OrderService
{
    private readonly MongoTopology _mongo;

    public OrderService(MongoTopology mongo) => _mongo = mongo;

    public sealed record CreateOrderResult(Order Order, bool WasCreated, bool UsedTransaction);

    /// <summary>
    /// Creates an order, or returns the original order when the idempotency
    /// key was already used. Returns <c>null</c> when the product doesn't
    /// exist or there isn't enough stock.
    /// </summary>
    public async Task<CreateOrderResult?> CreateOrderAsync(
        string rawIdempotencyKey, string productId, int quantity, CancellationToken ct = default)
    {
        var key = Idempotency.NormalizeKey(rawIdempotencyKey);

        // Fast path: the key was already used — return the original order.
        var existing = await _mongo.Orders.Find(o => o.IdempotencyKey == key).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return new CreateOrderResult(existing, WasCreated: false, UsedTransaction: false);
        }

        var product = await _mongo.Products.Find(p => p.Id == productId).FirstOrDefaultAsync(ct);
        if (product is null || product.Stock < quantity)
        {
            return null;
        }

        var order = new Order
        {
            IdempotencyKey = key,
            ProductId = product.Id,
            ProductSku = product.Sku,
            Quantity = quantity,
            Total = product.Price * quantity,
        };

        if (_mongo.IsReplicaSet)
        {
            using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
            try
            {
                await session.WithTransactionAsync(async (s, token) =>
                {
                    await _mongo.Orders.InsertOneAsync(s, order, cancellationToken: token);
                    var stock = await _mongo.Products.UpdateOneAsync(s,
                        p => p.Id == product.Id && p.Stock >= quantity,
                        Builders<Product>.Update.Inc(p => p.Stock, -quantity),
                        cancellationToken: token);
                    if (stock.ModifiedCount == 0)
                    {
                        throw new InvalidOperationException("Stock changed while the order was being placed.");
                    }
                    return order;
                }, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // A concurrent retry won the race; the unique index did its job.
                var winner = await _mongo.Orders.Find(o => o.IdempotencyKey == key).FirstOrDefaultAsync(ct);
                return winner is null ? null : new CreateOrderResult(winner, WasCreated: false, UsedTransaction: true);
            }

            return new CreateOrderResult(order, WasCreated: true, UsedTransaction: true);
        }

        // Standalone fallback: same writes, no transaction. The unique index
        // still protects against a duplicate order for this key.
        try
        {
            await _mongo.Orders.InsertOneAsync(order, cancellationToken: ct);
            await _mongo.Products.UpdateOneAsync(
                p => p.Id == product.Id && p.Stock >= quantity,
                Builders<Product>.Update.Inc(p => p.Stock, -quantity),
                cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var winner = await _mongo.Orders.Find(o => o.IdempotencyKey == key).FirstOrDefaultAsync(ct);
            return winner is null ? null : new CreateOrderResult(winner, WasCreated: false, UsedTransaction: false);
        }

        return new CreateOrderResult(order, WasCreated: true, UsedTransaction: false);
    }
}
