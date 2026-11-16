# Aggregation pipelines

MongoDB's answer to `GROUP BY` — and the part of MongoDB that SQL Server devs either love immediately or fight for a week. This doc explains the pipelines in `dotnet-mongodb`; the code lives in `AnalyticsService`.

## How to read a pipeline

A pipeline is an array of stages. Documents flow through stage 1, the output flows into stage 2, and so on — like a Unix pipe. Each stage is one BSON document whose key is the operator:

```json
[
  { "$match": { "Category": "Audio" } },
  { "$group": { "_id": "$Category", "Revenue": { "$sum": "$Total" } } },
  { "$sort": { "Revenue": -1 } }
]
```

Field references start with `$` (`$Total` means "this document's Total field"). `_id` in a `$group` is the group key — it is not the document id, it is whatever you group by. Accumulators (`$sum`, `$avg`, `$count`) live inside `$group`.

## The pipelines in this repo

**Revenue by category** — `$lookup` joins each order to its product, `$unwind` flattens the joined array, `$group` sums by category:

```
orders → $lookup products → $unwind → $group by ProductDoc.Category → $sort
```

**Top products** — `$group` first (cheap: it runs on the orders collection alone), then `$sort`, `$limit`, and only *then* `$lookup` the names. Ordering the stages this way means the join runs on 5 documents, not 5,000. Stage order is a performance decision, not a style choice.

**Average rating** — a single `$group` on the products collection. The simplest pipeline is often the right one.

## The `$lookup` join trap

`$lookup` matches on **BSON type equality**. If `Order.ProductId` is stored as a string and `Product._id` is an ObjectId, the join returns empty arrays — no error, no warning, just missing data. This repo stores `Order.ProductId` as an ObjectId for exactly this reason, and the integration tests assert the join returns rows. If your `$lookup` ever returns empty, check the types first.

## Pipeline vs application code

A pipeline runs on the server, in one round trip, over indexes. The alternative — load the documents and group them in C# — costs a round trip per document and your process's memory. Prefer the pipeline when:

- the grouping/filtering reduces the data (5 rows out, 5,000 in),
- the logic is the same for every caller,
- you can express it in stages without reaching for `$function` (JavaScript in a pipeline is a smell).

Prefer application code when the logic needs C# types, external calls, or branching that stages express badly. The `/compare` endpoint is the honest version of this trade: the driver and the EF Core provider run the *same* query, and the timings tell you which path is faster for that query shape.

## Testing pipelines

The pipelines in this repo are static arrays of `BsonDocument` stages, so the unit tests assert on their exact shape — `$lookup` from the right collection, `$group` on the right field — without a server. The integration tests then run the same stages against real data and assert the numbers. A wrong field name fails the unit test; a wrong assumption about the data fails the integration test. Both failures are cheap.
