# MongoDB in .NET 8 — Driver, Aggregation, Change Streams, Transactions

[![CI](https://github.com/varunkonnojutech-lab/dotnet-mongodb/actions/workflows/ci.yml/badge.svg)](https://github.com/varunkonnojutech-lab/dotnet-mongodb/actions/workflows/ci.yml)
[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/varunkonnojutech-lab/dotnet-mongodb)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)

Most .NET developers learn databases on SQL Server — and then meet MongoDB on a real project and discover the document model changes how you think about data. No joins to read a product, no migrations to add a field, queries that run as pipelines instead of SQL. This repo is a runnable lab for all of it, built around a small product catalog API in .NET 8 with the official MongoDB driver.

It is the companion to [`dotnet-postgresql`](https://github.com/varunkonnojutech-lab/dotnet-postgresql): same seed data, same endpoints, same idempotency demo — so you can compare the two databases feature by feature instead of reading about them in the abstract.

## Who this is for

- **.NET developers who know SQL Server** and keep hearing "we use Mongo" in job descriptions. Start with `docs/mongo-for-sqlserver-devs.md` — it maps what you know.
- **Backend engineers** who need schemaless attributes, text search, or idempotent writes without adding Elasticsearch, Redis, or a message broker.
- **Interview prep**: "How do you make an API idempotent?" and "When would you use MongoDB over SQL Server?" are senior-level questions. This repo is the answer, with code.

## What you'll learn

1. **The official driver, properly** — typed collections, `ObjectId` handling (stored as ObjectId, exposed as a string), `Decimal128` for money, and why the driver — not a generic ORM — is the default way to talk to MongoDB from .NET.
2. **Text search without a search service** — a `$text` index over name + description, results ranked by text score. And its limit: `$text` matches whole words, not prefixes — the tests prove it.
3. **Schemaless attributes** — the `Specs` dictionary is a document inside the document, queried with dot notation (`Specs.color == "black"`) without loading the row.
4. **Aggregation pipelines** — `$lookup` + `$group` revenue by category, top products by units sold, average rating per category. The pipelines are plain BSON stage arrays, so the unit tests assert on their exact shape.
5. **Multi-document transactions** — order insert + stock decrement in one transaction, so a crash mid-order can't sell stock it never recorded. Plus the honest fallback: on a standalone server the same writes run sequentially, and the unique index is still the backstop.
6. **Idempotent writes** — normalized idempotency keys + a unique index, proven by a test that fires 8 concurrent requests with the same key and gets exactly one order.
7. **Change streams** — MongoDB's LISTEN/NOTIFY counterpart: a hosted service watches the products collection, records every insert/update/delete, and resumes from its last token after a restart.
8. **TTL indexes** — change history is a rolling 7-day window, not a table that grows forever.
9. **Driver vs EF Core provider, measured** — the same query through both, timed, on the same collection. See `/compare`.

## The demo

A product catalog API. Eight seeded products with varied tags and specs, so every query shape has something to find.

| Endpoint | What it shows |
|---|---|
| `GET /products?search=wireless` | `$text` search, ranked by text score |
| `GET /products?tag=usb-c` | Array containment |
| `GET /products?specKey=color&specValue=black` | Dot-notation query into `Specs` |
| `GET /products/{id}/specs/{key}` | One schemaless value without loading the document |
| `POST /products` | Insert — the change stream sees it |
| `POST /orders` + `Idempotency-Key` header | Idempotent order in a multi-document transaction |
| `GET /analytics/revenue-by-category` | `$lookup` + `$group` aggregation |
| `GET /analytics/top-products` | `$group` + `$sort` + `$limit` + `$lookup` |
| `GET /analytics/average-rating` | `$group` with `$avg` |
| `GET /compare?category=Audio` | Driver vs EF Core MongoDB provider, timed |
| `GET /changes` | Recent change-stream events |
| `GET /health` | Topology: replica set or standalone, transactions available or not |

## Quick start

### Option A — everything in Docker

```bash
docker compose up --build
```

That starts MongoDB 7 as a single-node replica set (transactions and change streams need one — a plain `mongod` won't do), initiates the set, builds the API, creates the indexes, and seeds sample data.

- Swagger: <http://localhost:8080/swagger>
- Health: <http://localhost:8080/health>
- Search: <http://localhost:8080/products?search=wireless>
- Tag filter: <http://localhost:8080/products?tag=usb-c>
- Spec filter: <http://localhost:8080/products?specKey=color&specValue=black>

### Option B — MongoDB in Docker, .NET on your machine

```bash
docker compose up -d mongo mongo-init

# Run the API (it creates indexes and seeds on startup)
dotnet run --project src/MongoDemo
```

The API connects to `mongodb://localhost:27017/mongodemo` by default. Against a replica set, add `?replicaSet=rs0` to the connection string — the app detects the topology and enables transactions and change streams only when a set is present. Override the connection with `ConnectionStrings__MongoDb`.

Try the idempotency demo with the included `MongoDemo.http` file, or:

```bash
# First call creates the order (201)
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: order-2026-11-16-001" \
  -d '{"productId": "<id from GET /products>", "quantity": 2}'

# Replay with the same key returns the SAME order (200, no second charge)
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: order-2026-11-16-001" \
  -d '{"productId": "<same id>", "quantity": 2}'
```

## Tests

29 tests across four suites:

- **Unit tests** (`IdempotencyTests`, `ProductQueryTests`, `AggregationPipelineTests`) — pure logic, no database: key normalization, filters rendered to BSON, pipeline stage shapes.
- **Integration tests** (`MongoIntegrationTests`) — 14 tests against a real MongoDB: `$text` search and ranking, tag and spec filters, idempotent orders (including 8 concurrent requests with the same key → exactly one order), stock checks, aggregation results, driver vs EF Core row-count parity, change-stream delivery, and index existence (text, unique, TTL).

```bash
dotnet test
```

The integration tests need a replica-set MongoDB: `docker compose up -d mongo mongo-init` first, or set `MONGO_TEST_CONNECTION_STRING`. In CI they run against a single-node replica set started in Docker. Transactions and change streams are replica-set behaviour — faking them would test nothing.

## Docs

- [`docs/mongo-for-sqlserver-devs.md`](docs/mongo-for-sqlserver-devs.md) — documents vs tables, ObjectId vs identity columns, `$text` vs `CONTAINS`, transactions vs `BEGIN TRAN`, and when to pick which database.
- [`docs/aggregation-pipelines.md`](docs/aggregation-pipelines.md) — how to read a pipeline, the `$lookup` join trap (string keys silently join nothing), and when a pipeline beats application code.

## Why this repo exists

MongoDB is on the resume now, and the ladder had no repo for it. SQL Server is covered in depth by `sqlserver-efcore-pro`, PostgreSQL by `dotnet-postgresql` — this is the document-database third leg, and the three docs cross-reference each other. If you work in .NET long enough, you will meet all three.

## Next steps

- Add a `$facet` pipeline that returns top products, revenue, and rating stats in a single round trip.
- Try a multi-document transaction that spans two collections with a deliberate failure — watch the whole thing roll back.
- Swap the in-memory change-event store for a SignalR hub and push product changes to browsers live — see `signalr-realtime`.
- Compare the EF Core provider's LINQ translation with the driver's on a `$lookup` query and note where the provider gives up.
