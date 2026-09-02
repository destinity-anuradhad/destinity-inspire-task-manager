using TaskTracker.Services;
using TaskTracker.Tests.Helpers;
using Xunit;

namespace TaskTracker.Tests;

public class IdempotencyStoreTests
{
    [Fact]
    public async Task Save_then_TryGet_ReturnsCachedResponseScopedToClientAndKey()
    {
        using var db = DbContextFactory.Create(nameof(Save_then_TryGet_ReturnsCachedResponseScopedToClientAndKey));
        var store = new IdempotencyStore(db);

        Assert.Null(await store.TryGetAsync(1, "k1"));

        await store.SaveAsync(1, "k1", "POST", "/api/v1/tasks", 201, "{\"id\":5}");

        var hit = await store.TryGetAsync(1, "k1");
        Assert.NotNull(hit);
        Assert.Equal(201, hit!.StatusCode);
        Assert.Equal("{\"id\":5}", hit.ResponseBody);

        Assert.Null(await store.TryGetAsync(2, "k1"));   // scoped per client
        Assert.Null(await store.TryGetAsync(1, "k2"));   // scoped per key
    }
}
