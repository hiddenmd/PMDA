using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PMDA.Api.Auth;
using PMDA.Data;

namespace PMDA.Api.Tests.Auth;

public sealed class FileUserStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "pmda-users", Guid.NewGuid().ToString("N"), "users.json");
    private readonly DateTime _now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private FileUserStore Store(Func<CancellationToken, Task<IReadOnlyList<UserRecord>>>? legacy = null) =>
        new(Options.Create(new AuthOptions { UsersPath = _path }), NullLogger<FileUserStore>.Instance, legacy, TimeProvider.System);

    private UserRecord User(string name, UserRole role = UserRole.Analyst) =>
        new(Guid.NewGuid(), name, name, "hash", role, true, 0, null, Guid.NewGuid(), _now, null);

    [Fact]
    public async Task Creates_users_with_unique_names_regardless_of_case()
    {
        var store = Store();
        Assert.True(await store.CreateAsync(User("ana"), default));
        Assert.False(await store.CreateAsync(User("ANA"), default));
        Assert.Equal(1, await store.CountAsync(default));
        Assert.NotNull(await store.FindByNameAsync("Ana", default));
    }

    [Fact]
    public async Task Password_changes_rotate_the_stamp_and_clear_lockout()
    {
        var store = Store();
        var user = User("ana");
        await store.CreateAsync(user, default);
        await store.RecordFailureAsync(user.UserId, 2, _now.AddMinutes(15), default);
        await store.RecordFailureAsync(user.UserId, 2, _now.AddMinutes(15), default);
        var locked = (await store.GetAsync(user.UserId, default))!;
        Assert.Equal((2, _now.AddMinutes(15)), (locked.FailedAttempts, locked.LockoutUntilUtc));

        await store.SetPasswordAsync(user.UserId, "new-hash", default);
        var reset = (await store.GetAsync(user.UserId, default))!;
        Assert.Equal(("new-hash", 0, (DateTime?)null), (reset.PasswordHash, reset.FailedAttempts, reset.LockoutUntilUtc));
        Assert.NotEqual(user.SecurityStamp, reset.SecurityStamp);

        await store.UpgradeHashAsync(user.UserId, "newer-hash", default);
        Assert.Equal(reset.SecurityStamp, (await store.GetAsync(user.UserId, default))!.SecurityStamp);
    }

    [Fact]
    public async Task Updates_rotate_the_stamp_only_for_role_or_active_changes()
    {
        var store = Store();
        var user = User("ana");
        await store.CreateAsync(user, default);
        await store.UpdateAsync(user.UserId, "Ana B", UserRole.Analyst, true, default);
        Assert.Equal(user.SecurityStamp, (await store.GetAsync(user.UserId, default))!.SecurityStamp);
        await store.UpdateAsync(user.UserId, "Ana B", UserRole.Admin, true, default);
        var promoted = (await store.GetAsync(user.UserId, default))!;
        Assert.NotEqual(user.SecurityStamp, promoted.SecurityStamp);
        Assert.Equal(1, await store.ActiveAdminCountAsync(default));

        await store.RecordSuccessAsync(user.UserId, _now, default);
        Assert.Equal(_now, (await store.GetAsync(user.UserId, default))!.LastLoginUtc);
    }

    [Fact]
    public async Task Users_survive_a_restart()
    {
        var user = User("ana", UserRole.Admin);
        await Store().CreateAsync(user, default);
        var reread = await Store().GetAsync(user.UserId, default);
        Assert.Equal(user, reread);
    }

    [Fact]
    public async Task Migrates_once_from_the_legacy_source()
    {
        var legacy = new[] { User("ana", UserRole.Admin), User("bob") };
        var store = Store(_ => Task.FromResult<IReadOnlyList<UserRecord>>(legacy));
        await store.EnsureMigratedAsync(default);
        Assert.False(store.MigrationPending);
        Assert.Equal(2, await store.CountAsync(default));
        Assert.True(File.Exists(_path));

        // The file exists now: the legacy source is never read again.
        var again = Store(_ => throw new InvalidOperationException("must not be called"));
        await again.EnsureMigratedAsync(default);
        Assert.Equal(2, await again.CountAsync(default));
    }

    [Fact]
    public async Task An_unreachable_legacy_source_leaves_the_migration_pending()
    {
        var store = Store(_ => throw new TimeoutException("SQL Server did not answer"));
        await store.EnsureMigratedAsync(default);
        Assert.True(store.MigrationPending);
        Assert.Contains("SQL Server did not answer", store.MigrationError);
        Assert.False(File.Exists(_path));
        Assert.Equal(0, await store.CountAsync(default));
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
