using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore.Extensions;

namespace MongoDemo.Data;

/// <summary>
/// The EF Core MongoDB provider's view of the same <c>products</c>
/// collection. Only the fields the comparison query needs — the point of the
/// <c>/compare</c> endpoint is the translation, not a second full model.
/// <para>
/// The provider maps <see cref="ObjectId"/> keys natively; the driver's
/// string-typed <c>Product.Id</c> and this <c>ObjectId</c> key are the same
/// stored value, so both APIs read the same documents.
/// </para>
/// </summary>
public sealed class EfProduct
{
    public ObjectId Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

public sealed class MongoDbContext : DbContext
{
    public DbSet<EfProduct> Products => Set<EfProduct>();

    public MongoDbContext(DbContextOptions<MongoDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfProduct>().ToCollection("products");
    }
}
