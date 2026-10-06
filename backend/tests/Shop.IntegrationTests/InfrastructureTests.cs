using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class InfrastructureTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ready_returns_200_when_database_is_up() =>
        Assert.Equal(HttpStatusCode.OK, (await f.CreateGuest().GetAsync("/healthz/ready")).StatusCode);

    [Fact]
    public async Task UnitOfWork_rolls_back_when_work_throws()
    {
        using var scope = f.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        await Assert.ThrowsAsync<ApiException>(() => uow.ExecuteAsync<int>(async ct =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT shared.ProcessedMessages (MessageId, Handler) VALUES ({Guid.NewGuid()}, {"t"})", ct);
            throw new ApiException(400, "x");
        }, default));
        Assert.Equal(0, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.ProcessedMessages"));
    }

    [Fact]
    public async Task UnitOfWork_commits_when_work_succeeds()
    {
        using var scope = f.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        await uow.ExecuteAsync(async ct =>
        {
            db.ProcessedMessages.Add(new ProcessedMessage { MessageId = Guid.NewGuid(), Handler = "t" });
            return await db.SaveChangesAsync(ct);
        }, default);
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.ProcessedMessages"));
    }

    [Fact]
    public async Task Unknown_route_error_is_problem_details_without_stack_trace()
    {
        var r = await f.CreateGuest().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await r.CodeAsync());
        Assert.DoesNotContain("   at ", await r.Content.ReadAsStringAsync());
    }
}
