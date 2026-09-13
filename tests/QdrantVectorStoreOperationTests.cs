using Argentini.Umbraco.Search.Qdrant.Indexers;
using Argentini.Umbraco.Search.Qdrant.VectorStores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Umbraco.AI.Search.Core.VectorStore;

namespace Umbraco.Search.Qdrant.Tests;

public sealed class QdrantVectorStoreOperationTests
{
    [Fact]
    public async Task UpsertAsync_ReturnsWithoutQdrantCallWhenIndexIsUnknown()
    {
        var store = CreateOfflineStore(new AiSearchIndexFilterOptions { DisableDefaultIndex = true });

        await store.UpsertAsync("unknown", "document", null, 0, new ReadOnlyMemory<float>([1f, 0f, 0f]));
    }

    [Fact]
    public async Task UpsertManyAsync_ReturnsWithoutQdrantCallForEmptyBatch()
    {
        var store = CreateOfflineStore(new AiSearchIndexFilterOptions
        {
            DisableDefaultIndex = false,
            Connection = { EmbeddingSize = 3 }
        });

        await store.UpsertManyAsync("UmbAI_Search", "document", []);
    }

    [Fact]
    public async Task UpsertManyAsync_ThrowsWhenEntryVectorDimensionDoesNotMatch()
    {
        var store = CreateOfflineStore(new AiSearchIndexFilterOptions
        {
            DisableDefaultIndex = false,
            Connection = { EmbeddingSize = 3 }
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertManyAsync(
                "UmbAI_Search",
                "document",
                [new AIVectorEntry("document", null, 0, new ReadOnlyMemory<float>([1f, 0f]), null)]));
    }

    [Fact]
    public async Task SearchAsync_QdrantUnavailable_ReturnsEmptyResults()
    {
        var logger = new RecordingLogger<QdrantVectorStore>();
        var store = CreateOfflineStore(new AiSearchIndexFilterOptions
        {
            DisableDefaultIndex = false,
            Connection = { EmbeddingSize = 3 }
        }, logger);

        var results = await store.SearchAsync(
            "UmbAI_Search",
            new ReadOnlyMemory<float>([1f, 0f, 0f]));

        Assert.Empty(results);
        Assert.Equal(LogLevel.Warning, logger.LastLevel);
        Assert.NotNull(logger.LastException);
    }

    private static QdrantVectorStore CreateOfflineStore(
        AiSearchIndexFilterOptions options,
        ILogger<QdrantVectorStore>? logger = null) =>
        new(
            new QdrantClient("127.0.0.1", 1),
            Options.Create(options),
            logger ?? NullLogger<QdrantVectorStore>.Instance);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public LogLevel? LastLevel { get; private set; }

        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastLevel = logLevel;
            LastException = exception;
        }
    }
}
