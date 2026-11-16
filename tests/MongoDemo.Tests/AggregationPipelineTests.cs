using MongoDB.Bson;
using MongoDemo.Services;

namespace MongoDemo.Tests;

/// <summary>
/// The aggregation pipelines are static BSON stage arrays — these tests
/// assert on the exact stage shapes, so a wrong field name or a missing
/// $unwind fails here, not against a live database.
/// </summary>
public sealed class AggregationPipelineTests
{
    [Fact]
    public void RevenueByCategory_LooksUpProducts_ThenGroupsByCategory()
    {
        var stages = AnalyticsService.RevenueByCategoryStages();

        Assert.Equal(4, stages.Length);
        Assert.Equal("products", stages[0]["$lookup"]["from"].AsString);
        Assert.Equal("ProductId", stages[0]["$lookup"]["localField"].AsString);
        Assert.Equal("_id", stages[0]["$lookup"]["foreignField"].AsString);
        Assert.Equal("$ProductDoc", stages[1]["$unwind"].AsString);
        Assert.Equal("$ProductDoc.Category", stages[2]["$group"]["_id"].AsString);
        Assert.Equal("$Total", stages[2]["$group"]["Revenue"]["$sum"].AsString);
        Assert.Equal(-1, stages[3]["$sort"]["Revenue"].AsInt32);
    }

    [Fact]
    public void TopProducts_GroupsSortsLimits_ThenLooksUpNames()
    {
        var stages = AnalyticsService.TopProductsStages(5);

        Assert.Equal(6, stages.Length);
        Assert.Equal("$ProductId", stages[0]["$group"]["_id"].AsString);
        Assert.Equal("$Quantity", stages[0]["$group"]["UnitsSold"]["$sum"].AsString);
        Assert.Equal(-1, stages[1]["$sort"]["UnitsSold"].AsInt32);
        Assert.Equal(5, stages[2]["$limit"].AsInt32);
        Assert.Equal("products", stages[3]["$lookup"]["from"].AsString);
        Assert.Equal("$ProductDoc", stages[4]["$unwind"].AsString);
        Assert.Equal("$ProductDoc.Sku", stages[5]["$project"]["Sku"].AsString);
    }

    [Fact]
    public void TopProducts_RespectsLimit()
    {
        Assert.Equal(3, AnalyticsService.TopProductsStages(3)[2]["$limit"].AsInt32);
    }

    [Fact]
    public void AverageRating_GroupsByCategory_AveragesRating()
    {
        var stages = AnalyticsService.AverageRatingStages();

        Assert.Equal(2, stages.Length);
        Assert.Equal("$Category", stages[0]["$group"]["_id"].AsString);
        Assert.Equal("$Rating", stages[0]["$group"]["AverageRating"]["$avg"].AsString);
        Assert.Equal(-1, stages[1]["$sort"]["AverageRating"].AsInt32);
    }
}
