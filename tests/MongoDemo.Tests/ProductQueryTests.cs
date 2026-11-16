using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDemo.Models;
using MongoDemo.Services;

namespace MongoDemo.Tests;

/// <summary>
/// The catalog queries are proven by rendering their filters to BSON — no
/// server needed. If a field name is wrong, this fails at build-test time,
/// not in production.
/// </summary>
public sealed class ProductQueryTests
{
    private static string Render(FilterDefinition<Product> filter) =>
        filter.Render(new RenderArgs<Product>(
            BsonSerializer.SerializerRegistry.GetSerializer<Product>(),
            BsonSerializer.SerializerRegistry)).ToJson();

    [Fact]
    public void BuildFilter_Empty_ReturnsEmptyDocument()
    {
        Assert.Equal("{ }", Render(ProductService.BuildFilter(null, null, null, null)));
    }

    [Fact]
    public void BuildFilter_Search_UsesTextOperator()
    {
        var json = Render(ProductService.BuildFilter("wireless headphones", null, null, null));
        Assert.Contains("$text", json);
        Assert.Contains("wireless headphones", json);
    }

    [Fact]
    public void BuildFilter_Tag_UsesArrayContainment()
    {
        // $eq on an array field matches documents where the array contains
        // the value — the driver's AnyEq renders exactly that.
        var json = Render(ProductService.BuildFilter(null, "usb-c", null, null));
        Assert.Contains("Tags", json);
        Assert.Contains("usb-c", json);
    }

    [Fact]
    public void BuildFilter_Spec_UsesDotNotation()
    {
        var json = Render(ProductService.BuildFilter(null, null, "color", "black"));
        Assert.Contains("Specs.color", json);
        Assert.Contains("black", json);
    }

    [Fact]
    public void BuildFilter_SpecKeyWithoutValue_IsIgnored()
    {
        Assert.Equal("{ }", Render(ProductService.BuildFilter(null, null, "color", null)));
    }

    [Fact]
    public void BuildFilter_AllFilters_CombinedAtTopLevel()
    {
        // The driver flattens the $and into one document — MongoDB applies
        // implicit AND to top-level fields — and $text is hoisted to the
        // top level because MongoDB forbids $text inside $and.
        var json = Render(ProductService.BuildFilter("wireless", "usb-c", "color", "black"));
        Assert.Contains("$text", json);
        Assert.Contains("usb-c", json);
        Assert.Contains("Specs.color", json);
        Assert.DoesNotContain("$and", json);
    }
}
