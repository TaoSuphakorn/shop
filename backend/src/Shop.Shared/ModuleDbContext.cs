using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Shared;

/// <summary>Base for every module's DbContext: uses the scope's UnitOfWork connection and transaction.</summary>
public abstract class ModuleDbContext : DbContext
{
    protected ModuleDbContext(DbContextOptions options, UnitOfWork unitOfWork) : base(options) => unitOfWork.Enlist(this);

    // Every timestamp is stored as UTC. Reading it back as UTC makes JSON carry the "Z" suffix,
    // without which browsers would treat the reservation expiry as local time.
    protected override void ConfigureConventions(ModelConfigurationBuilder configuration) =>
        configuration.Properties<DateTime>().HaveConversion<UtcConverter>();

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}

public static class ModuleDbContextExtensions
{
    public static IServiceCollection AddModuleDbContext<T>(this IServiceCollection services, string schema) where T : ModuleDbContext =>
        services
            .AddDbContext<T>((sp, options) => options.UseSqlServer(
                sp.GetRequiredService<UnitOfWork>().Connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema)))   // no EnableRetryOnFailure: UnitOfWork owns retries
            .AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<T>());
}
