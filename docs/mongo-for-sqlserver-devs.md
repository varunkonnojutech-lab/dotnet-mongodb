# MongoDB for SQL Server devs

A field guide for .NET developers who know SQL Server and are meeting MongoDB for the first time. The `dotnet-mongodb` repo is the runnable version of this doc; `dotnet-postgresql` and `sqlserver-efcore-pro` are the other two legs of the same comparison.

## The mental model

| SQL Server | MongoDB | What changes |
|---|---|---|
| Database | Database | Same word, same idea |
| Table | Collection | A collection holds documents, not rows |
| Row | Document | A document is a JSON-like object, self-contained |
| Column | Field | Fields vary per document — no schema to alter |
| Primary key | `_id` | An `ObjectId` by default: 12 bytes, roughly time-ordered |
| Foreign key + JOIN | Embedded document, or `$lookup` | Embedding is the default; `$lookup` is the escape hatch |
| `BEGIN TRAN` | Multi-document transaction | Needs a replica set — a standalone `mongod` has no transactions |
| Trigger | Change stream | A resumable stream of every insert/update/delete |
| Full-text index | `$text` index | Whole-word matching, ranked by text score |

## Documents vs tables

In SQL Server you normalize: a product row, a separate specs table, a tags table, joins to read them back. In MongoDB you embed: the product, its tags, and its specs are one document, read in one round trip.

```json
{
  "_id": { "$oid": "691a..." },
  "sku": "HP-300",
  "name": "Aurora Wireless Headphones",
  "tags": ["wireless", "bluetooth"],
  "specs": { "color": "black", "batteryHours": "40" },
  "price": { "$numberDecimal": "199.99" }
}
```

The trade: reads get simpler, writes get wider. Updating a product's name touches one document instead of one row — but there is no `UPDATE ... JOIN`, and a "product with all its orders" query is a `$lookup` pipeline, not a join you write in your sleep.

## Identity: ObjectId vs IDENTITY

- **SQL Server**: `INT IDENTITY(1,1)` — small, sequential, human-readable. Gaps after rollbacks, and a pain to generate on the client.
- **MongoDB**: `ObjectId` — 12 bytes (timestamp + machine + process + counter), generated on the client before the insert. No round trip to learn the key, no sequence to coordinate.

In this repo `Product.Id` is a C# `string` holding the 24-hex form, stored as an ObjectId via `[BsonRepresentation(BsonType.ObjectId)]`. The API speaks strings; the database stores ObjectIds.

## Types that bite

| C# / SQL Server | MongoDB | Gotcha |
|---|---|---|
| `decimal` / `decimal(18,2)` | `Decimal128` | Without `[BsonRepresentation(BsonType.Decimal128)]`, a `decimal` serializes as a `Double` and you lose exact money math. The repo marks every money field. |
| `DateTime` / `datetime2` | BSON `Date` (UTC) | The driver stores `DateTime` as UTC. A `DateTimeKind.Local` value is converted silently — always use `DateTime.UtcNow`. |
| `bool` / `bit` | `Boolean` | Same. |
| `string` / `nvarchar` | `String` | Same — but a C# string key in a `$lookup` **does not match** an ObjectId. See the `$lookup` trap below. |

## Querying: LINQ-ish, but not LINQ

The driver has a LINQ provider (`collection.AsQueryable()`), but this repo uses the builders, because what you write is what MongoDB runs:

```csharp
// SQL: WHERE Category = 'Audio' AND Price < 100
Builders<Product>.Filter.Eq(p => p.Category, "Audio")
    & Builders<Product>.Filter.Lt(p => p.Price, 100m);

// SQL: WHERE Tags contains 'usb-c' — no junction table, no join
Builders<Product>.Filter.AnyEq(p => p.Tags, "usb-c");

// SQL: full-text search — CONTAINS(Name, 'wireless')
Builders<Product>.Filter.Text("wireless");
```

`$text` has one limit SQL devs trip over: it matches **whole words** (with stemming), not prefixes. Searching `wireles` finds nothing; `wireless` finds the product. There is no `LIKE 'wireles%'` equivalent in `$text` — for prefix matching you need a regex, which can't use the text index.

## The `$lookup` trap

`$lookup` joins on **BSON type equality**, not string equality. An `Order` whose `ProductId` is the *string* `"691a..."` will never join to a `Product` whose `_id` is the *ObjectId* `ObjectId("691a...")` — the pipeline runs, returns empty joins, and nothing errors. This repo stores `Order.ProductId` as an ObjectId (`[BsonRepresentation(BsonType.ObjectId)]`) for exactly this reason, and the integration tests assert the join returns rows.

## Transactions

```sql
-- SQL Server
BEGIN TRAN;
INSERT INTO Orders ...;
UPDATE Products SET Stock = Stock - 1 ...;
COMMIT;
```

```csharp
// MongoDB — same shape, but the server must be a replica set
using var session = await client.StartSessionAsync();
await session.WithTransactionAsync(async (s, token) =>
{
    await orders.InsertOneAsync(s, order, cancellationToken: token);
    await products.UpdateOneAsync(s, filter, update, cancellationToken: token);
    return order;
});
```

A standalone `mongod` rejects transactions outright. That is why the repo's Docker Compose runs a single-node replica set, and why the app detects the topology at startup and falls back to sequential writes (with the unique index as the backstop) when there is no set. `WithTransactionAsync` also retries the whole transaction on transient errors — write conflicts between concurrent transactions are retried, not surfaced.

## Change streams vs triggers

A SQL Server trigger runs inside the transaction, synchronously, in T-SQL. A change stream is the opposite: an asynchronous, resumable cursor over the oplog. The repo's `ChangeStreamService` watches the products collection, records every change, and stores the resume token so a restart picks up where it left off instead of replaying history. Inserts that happen while the service is down are *not* lost — the stream replays from the token — but they are not instant either: expect sub-second, not same-transaction, delivery.

## When to pick which

- **Pick MongoDB** when the data is naturally a document (catalogs, user profiles, event payloads), when the schema evolves per record, or when you need schemaless attributes without `ALTER TABLE` migrations.
- **Pick SQL Server / PostgreSQL** when the data is naturally relational (ledger entries, anything with multi-table integrity rules), when you need joins across many tables in one query, or when the team already thinks in SQL.
- **Pick both** more often than you'd think: the catalog in MongoDB, the orders ledger in SQL Server, synced by the change stream. That's a real architecture, not a compromise.
