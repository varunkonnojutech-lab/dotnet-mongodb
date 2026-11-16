using MongoDB.Bson;
using MongoDB.Driver;
using MongoDemo.Models;

namespace MongoDemo.Services;

/// <summary>In-memory ring buffer of the most recent change events, for GET /changes.</summary>
public sealed class ChangeEventStore
{
    private readonly List<ChangeEvent> _events = [];
    private readonly object _lock = new();

    public void Add(ChangeEvent changeEvent)
    {
        lock (_lock)
        {
            _events.Add(changeEvent);
            if (_events.Count > 50)
            {
                _events.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<ChangeEvent> Recent()
    {
        lock (_lock)
        {
            return _events.AsReadOnly();
        }
    }
}

/// <summary>
/// Watches the products collection with a change stream — MongoDB's
/// LISTEN/NOTIFY counterpart (see <c>dotnet-postgresql</c>). Every insert,
/// update, replace and delete lands in the <see cref="ChangeEventStore"/> and
/// the <c>change_events</c> collection (which has a 7-day TTL index).
/// <para>
/// Change streams need a replica set, so this service stays idle on a
/// standalone server. It also keeps the resume token: on restart it resumes
/// after the last event it saw instead of replaying the whole stream.
/// </para>
/// </summary>
public sealed class ChangeStreamService : BackgroundService
{
    private readonly MongoTopology _mongo;
    private readonly ChangeEventStore _store;
    private readonly ILogger<ChangeStreamService> _logger;

    public ChangeStreamService(MongoTopology mongo, ChangeEventStore store, ILogger<ChangeStreamService> logger)
    {
        _mongo = mongo;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_mongo.IsReplicaSet)
        {
            _logger.LogInformation("Change stream disabled: standalone MongoDB has no change streams. Run the replica-set compose file to enable it.");
            return;
        }

        // Resume after the last persisted event, so a restart doesn't replay.
        BsonDocument? resumeToken = null;
        var last = await _mongo.ChangeEvents.Find(FilterDefinition<ChangeEvent>.Empty)
            .SortByDescending(e => e.SeenAt)
            .Project(e => e.ResumeToken)
            .FirstOrDefaultAsync(stoppingToken);
        if (last is not null)
        {
            resumeToken = BsonDocument.Parse(last);
        }

        var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup };
        if (resumeToken is not null)
        {
            options.ResumeAfter = resumeToken;
        }

        _logger.LogInformation("Change stream watching products (resuming: {Resuming}).", resumeToken is not null);

        try
        {
            using var cursor = await _mongo.Products.WatchAsync(options, stoppingToken);
            await cursor.ForEachAsync(async change =>
            {
                var product = change.FullDocument;
                var changeEvent = new ChangeEvent
                {
                    OperationType = change.OperationType.ToString().ToLowerInvariant(),
                    ProductId = product?.Id ?? change.DocumentKey["_id"].ToString() ?? string.Empty,
                    ProductSku = product?.Sku ?? string.Empty,
                    ResumeToken = change.ResumeToken.ToJson(),
                };
                _store.Add(changeEvent);
                try
                {
                    await _mongo.ChangeEvents.InsertOneAsync(changeEvent, cancellationToken: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not persist change event {Operation} for {ProductId}.", changeEvent.OperationType, changeEvent.ProductId);
                }
            }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change stream ended unexpectedly.");
        }
    }
}
