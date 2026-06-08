using Argentini.Umbraco.Search.Qdrant.Indexers;
using Argentini.Umbraco.Search.Qdrant.VectorStores;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Umbraco.AI.Search.Core.VectorStore;
// ReSharper disable RedundantArgumentDefaultValue

namespace Umbraco.Search.Qdrant.Tests;

public sealed class QdrantVectorStoreIntegrationTests : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("qdrant/qdrant")
        .WithPortBinding(6334, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6334))
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesDefaultCollection()
    {
        var store = CreateStore(out var client);

        await store.InitializeAsync();

        var collections = await client.ListCollectionsAsync();
        Assert.Contains("umbraco-sfumato-umbai_search", collections);
    }

    [Fact]
    public async Task UpsertSearchAndDeleteDocument_WorkAgainstQdrant()
    {
        var store = CreateStore(out _);
        var indexName = UniqueIndexName();
        var documentId = Guid.NewGuid().ToString("D");

        await store.UpsertManyAsync(
            indexName,
            documentId,
            [
                new AIVectorEntry(
                    documentId,
                    "en-US",
                    0,
                    new ReadOnlyMemory<float>([1f, 0f, 0f]),
                    new Dictionary<string, object>
                    {
                        ["chunkIndex"] = 0,
                        ["category"] = "Docs",
                        ["snippet"] = "Plain snippet"
                    })
            ]);

        var results = await store.SearchAsync(
            indexName,
            new ReadOnlyMemory<float>([1f, 0f, 0f]),
            "en-US",
            10,
            new Dictionary<string, IReadOnlyCollection<object?>?> { ["category"] = ["Docs"] });

        var result = Assert.Single(results);
        Assert.Equal(documentId, result.DocumentId);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Plain snippet", result.Metadata["snippet"]);

        await store.DeleteDocumentAsync(indexName, documentId);

        var afterDelete = await store.SearchAsync(indexName, new ReadOnlyMemory<float>([1f, 0f, 0f]), "en-US", 10);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task GetVectorsByDocument_ReturnsStoredChunksAcrossCollections()
    {
        var store = CreateStore(out _);
        var indexName = UniqueIndexName();
        var documentId = Guid.NewGuid().ToString("D");

        await store.UpsertManyAsync(
            indexName,
            documentId,
            [
                new AIVectorEntry(documentId, null, 1, new ReadOnlyMemory<float>([0f, 1f, 0f]), new Dictionary<string, object> { ["chunkIndex"] = 1 }),
                new AIVectorEntry(documentId, "en-US", 0, new ReadOnlyMemory<float>([1f, 0f, 0f]), new Dictionary<string, object> { ["chunkIndex"] = 0 })
            ]);

        var entries = await store.GetVectorsByDocumentAsync(indexName, documentId);

        Assert.Equal([0, 1], entries.Select(entry => entry.ChunkIndex));
        Assert.All(entries, entry => Assert.Equal(documentId, entry.DocumentId));
    }

    [Fact]
    public async Task ResetAsync_ClearsExistingIndexCollections()
    {
        var store = CreateStore(out _);
        var indexName = UniqueIndexName();
        var documentId = Guid.NewGuid().ToString("D");

        await store.UpsertAsync(indexName, documentId, null, 0, new ReadOnlyMemory<float>([1f, 0f, 0f]));

        await store.ResetAsync(indexName);

        var results = await store.SearchAsync(indexName, new ReadOnlyMemory<float>([1f, 0f, 0f]), null, 10);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_MergesVariationFallbackCollectionsAndOrdersByScore()
    {
        var store = CreateStore(out _);
        var indexName = UniqueIndexName();
        var cultureDocumentId = Guid.NewGuid().ToString("D");
        var invariantDocumentId = Guid.NewGuid().ToString("D");

        await store.UpsertManyAsync(
            indexName,
            cultureDocumentId,
            [
                new AIVectorEntry(cultureDocumentId, "en-US", 0, new ReadOnlyMemory<float>([0.9f, 0.1f, 0f]), new Dictionary<string, object> { ["chunkIndex"] = 0 })
            ]);
        await store.UpsertManyAsync(
            indexName,
            invariantDocumentId,
            [
                new AIVectorEntry(invariantDocumentId, null, 0, new ReadOnlyMemory<float>([1f, 0f, 0f]), new Dictionary<string, object> { ["chunkIndex"] = 0 })
            ]);

        var results = await store.SearchAsync(indexName, new ReadOnlyMemory<float>([1f, 0f, 0f]), "en-US", 10);

        Assert.Equal([invariantDocumentId, cultureDocumentId], results.Select(result => result.DocumentId));
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenCollectionsDoNotExist()
    {
        var store = CreateStore(out _);

        var results = await store.SearchAsync(UniqueIndexName(), new ReadOnlyMemory<float>([1f, 0f, 0f]), null, 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ThrowsWhenQueryVectorDimensionDoesNotMatchCollection()
    {
        var store = CreateStore(out _);
        var indexName = UniqueIndexName();
        var documentId = Guid.NewGuid().ToString("D");

        await store.UpsertAsync(indexName, documentId, null, 0, new ReadOnlyMemory<float>([1f, 0f, 0f]));

        await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            store.SearchAsync(indexName, new ReadOnlyMemory<float>([1f, 0f]), null, 10));
    }

    [Fact]
    public async Task SearchAsync_FiltersRealQdrantPayloadsByBoolIntAndLongValues()
    {
        var store = CreateStore(out _);
        var indexName = UniqueIndexName();
        var matchingId = Guid.NewGuid().ToString("D");
        var wrongId = Guid.NewGuid().ToString("D");

        await store.UpsertManyAsync(
            indexName,
            matchingId,
            [
                new AIVectorEntry(
                    matchingId,
                    null,
                    0,
                    new ReadOnlyMemory<float>([1f, 0f, 0f]),
                    new Dictionary<string, object>
                    {
                        ["chunkIndex"] = 0,
                        ["published"] = true,
                        ["count"] = 7,
                        ["tenantId"] = 42L
                    })
            ]);
        await store.UpsertManyAsync(
            indexName,
            wrongId,
            [
                new AIVectorEntry(
                    wrongId,
                    null,
                    0,
                    new ReadOnlyMemory<float>([1f, 0f, 0f]),
                    new Dictionary<string, object>
                    {
                        ["chunkIndex"] = 0,
                        ["published"] = false,
                        ["count"] = 7,
                        ["tenantId"] = 42L
                    })
            ]);

        var results = await store.SearchAsync(
            indexName,
            new ReadOnlyMemory<float>([1f, 0f, 0f]),
            null,
            10,
            new Dictionary<string, IReadOnlyCollection<object?>?>
            {
                ["published"] = [true],
                ["count"] = [7],
                ["tenantId"] = [42L]
            });

        var result = Assert.Single(results);
        Assert.Equal(matchingId, result.DocumentId);
    }

    private QdrantVectorStore CreateStore(out QdrantClient client)
    {
        client = new QdrantClient("localhost", _container.GetMappedPublicPort(6334));

        return new QdrantVectorStore(
            client,
            Options.Create(new AiSearchIndexFilterOptions
            {
                DisableDefaultIndex = false,
                Connection = new QdrantConnectionOptions
                {
                    ServerPort = _container.GetMappedPublicPort(6334),
                    EmbeddingSize = 3
                }
            }),
            NullLogger<QdrantVectorStore>.Instance);
    }

    private static string UniqueIndexName() => "test_" + Guid.NewGuid().ToString("N");
}
