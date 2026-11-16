using MongoDB.Driver;
using MongoDemo.Models;
using MongoDemo.Services;

namespace MongoDemo.Data;

/// <summary>
/// Eight products with varied tags, specs and ratings, so every query shape
/// in the repo has something to find — the same seed set as
/// <c>dotnet-postgresql</c>, so the two companion repos compare directly.
/// </summary>
public static class SeedData
{
    public static List<Product> Products() =>
    [
        new()
        {
            Sku = "HP-300", Name = "Aurora Wireless Headphones",
            Description = "Noise-cancelling wireless headphones with 40-hour battery life and Bluetooth multipoint.",
            Category = "Audio", Price = 199.99m,
            Tags = ["wireless", "bluetooth", "noise-cancelling"],
            Specs = new() { ["color"] = "black", ["batteryHours"] = "40", ["bluetooth"] = "5.3" },
            Stock = 42, Rating = 4.7,
        },
        new()
        {
            Sku = "KB-120", Name = "Halo Mechanical Keyboard",
            Description = "Hot-swappable mechanical keyboard with RGB backlight and PBT keycaps.",
            Category = "Accessories", Price = 129.00m,
            Tags = ["usb-c", "rgb", "mechanical"],
            Specs = new() { ["color"] = "white", ["switches"] = "tactile", ["layout"] = "75%" },
            Stock = 87, Rating = 4.8,
        },
        new()
        {
            Sku = "MS-050", Name = "Orbit Wireless Mouse",
            Description = "Silent wireless mouse with 4000 DPI sensor and USB-C fast charging.",
            Category = "Accessories", Price = 49.99m,
            Tags = ["wireless", "usb-c", "silent"],
            Specs = new() { ["color"] = "black", ["dpi"] = "4000", ["batteryHours"] = "70" },
            Stock = 150, Rating = 4.5,
        },
        new()
        {
            Sku = "MN-270", Name = "Vista 27-inch 4K Monitor",
            Description = "27-inch 4K IPS monitor with HDR400, 144Hz refresh and USB-C hub.",
            Category = "Displays", Price = 349.99m,
            Tags = ["usb-c", "4k", "144hz"],
            Specs = new() { ["color"] = "silver", ["panel"] = "IPS", ["refreshHz"] = "144" },
            Stock = 23, Rating = 4.6,
        },
        new()
        {
            Sku = "SP-210", Name = "Echo Smart Speaker",
            Description = "Voice-controlled smart speaker with room-filling sound and multi-room sync.",
            Category = "Audio", Price = 89.99m,
            Tags = ["wireless", "voice", "multi-room"],
            Specs = new() { ["color"] = "charcoal", ["voiceAssistant"] = "built-in" },
            Stock = 64, Rating = 4.3,
        },
        new()
        {
            Sku = "WB-400", Name = "Atlas Laptop Stand",
            Description = "Aluminium laptop stand with adjustable height and passive cooling vents.",
            Category = "Accessories", Price = 59.00m,
            Tags = ["aluminium", "ergonomic"],
            Specs = new() { ["color"] = "silver", ["material"] = "aluminium" },
            Stock = 200, Rating = 4.4,
        },
        new()
        {
            Sku = "CB-090", Name = "Pulse Fitness Band",
            Description = "Fitness band with heart-rate tracking, sleep analysis and 14-day battery.",
            Category = "Wearables", Price = 79.99m,
            Tags = ["wireless", "bluetooth", "fitness"],
            Specs = new() { ["color"] = "black", ["batteryHours"] = "336", ["waterResistance"] = "5ATM" },
            Stock = 95, Rating = 4.2,
        },
        new()
        {
            Sku = "DK-015", Name = "Nimbus USB-C Dock",
            Description = "11-in-1 USB-C docking station with dual 4K output and 100W power delivery.",
            Category = "Accessories", Price = 179.99m,
            Tags = ["usb-c", "4k", "docking"],
            Specs = new() { ["color"] = "space-grey", ["ports"] = "11", ["powerDeliveryW"] = "100" },
            Stock = 31, Rating = 4.9,
        },
    ];

    public static async Task SeedAsync(MongoTopology mongo)
    {
        if (await mongo.Products.CountDocumentsAsync(FilterDefinition<Product>.Empty) > 0)
        {
            return;
        }

        await mongo.Products.InsertManyAsync(Products());
    }
}
