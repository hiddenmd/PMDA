using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PMDA.Data;

namespace PMDA.Api.Auth;

/// <summary>
/// User accounts in a local file (default %ProgramData%\PMDA\users.json), so signing in never depends on a flight
/// database. The accounts are held in memory; every change rewrites the file atomically with a restricted ACL.
/// On first start the accounts are copied once from SQL (<c>dbo.AppUser</c>) when a legacy source is given.
/// </summary>
public sealed class FileUserStore : IUserStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _migration = new(1, 1);
    private readonly string _path;
    private readonly ILogger<FileUserStore> _logger;
    private readonly Func<CancellationToken, Task<IReadOnlyList<UserRecord>>>? _legacy;
    private readonly TimeProvider _time;
    private List<UserRecord> _users = [];
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    public FileUserStore(IOptions<AuthOptions> options, ILogger<FileUserStore> logger,
        Func<CancellationToken, Task<IReadOnlyList<UserRecord>>>? legacy, TimeProvider time)
    {
        _path = options.Value.UsersPath is { Length: > 0 } path ? path : DefaultPath;
        _logger = logger;
        _time = time;
        if (File.Exists(_path))
            _users = JsonSerializer.Deserialize<List<UserRecord>>(File.ReadAllText(_path), Json) ?? [];
        else if (legacy is not null)
        {
            _legacy = legacy;
            MigrationPending = true;
        }
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PMDA", "users.json");

    /// <summary>The accounts are still in SQL and could not be copied yet (the database was unreachable).</summary>
    public bool MigrationPending { get; private set; }

    public string? MigrationError { get; private set; }

    /// <summary>Copies the SQL accounts if that has not happened yet; retried at most every 30 s.</summary>
    public async Task EnsureMigratedAsync(CancellationToken ct)
    {
        if (!MigrationPending)
            return;
        await _migration.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (!MigrationPending || now - _lastAttemptUtc < RetryEvery)
                return;
            _lastAttemptUtc = now;
            try
            {
                var users = await _legacy!(ct);
                lock (_gate)
                {
                    _users = users.ToList();
                    Save();
                }
                MigrationPending = false;
                MigrationError = null;
                _logger.LogWarning("Moved {Count} user account(s) from SQL Server to {Path}", users.Count, _path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MigrationError = ex.Message;
                _logger.LogError(ex, "Could not move the user accounts from SQL Server yet; will retry");
            }
        }
        finally
        {
            _migration.Release();
        }
    }

    public Task<int> CountAsync(CancellationToken ct) => Read(users => users.Count);

    public Task<int> ActiveAdminCountAsync(CancellationToken ct) =>
        Read(users => users.Count(u => u.Role == UserRole.Admin && u.IsActive));

    public Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken ct) =>
        Read<IReadOnlyList<UserRecord>>(users => users.OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<UserRecord?> GetAsync(Guid userId, CancellationToken ct) => Read(users => users.FirstOrDefault(u => u.UserId == userId));

    public Task<UserRecord?> FindByNameAsync(string userName, CancellationToken ct) =>
        Read(users => users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> CreateAsync(UserRecord user, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_users.Any(u => string.Equals(u.UserName, user.UserName, StringComparison.OrdinalIgnoreCase) || u.UserId == user.UserId))
                return Task.FromResult(false);
            _users.Add(user);
            Save();
            return Task.FromResult(true);
        }
    }

    public Task UpdateAsync(Guid userId, string displayName, UserRole role, bool isActive, CancellationToken ct) =>
        Change(userId, u => u with
        {
            SecurityStamp = u.Role != role || u.IsActive != isActive ? Guid.NewGuid() : u.SecurityStamp,
            DisplayName = displayName,
            Role = role,
            IsActive = isActive,
        });

    public Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct) =>
        Change(userId, u => u with { PasswordHash = passwordHash, SecurityStamp = Guid.NewGuid(), FailedAttempts = 0, LockoutUntilUtc = null });

    public Task UpgradeHashAsync(Guid userId, string passwordHash, CancellationToken ct) =>
        Change(userId, u => u with { PasswordHash = passwordHash });

    public Task RecordFailureAsync(Guid userId, int maxAttempts, DateTime lockoutUntilUtc, CancellationToken ct) =>
        Change(userId, u => u with
        {
            FailedAttempts = u.FailedAttempts + 1,
            LockoutUntilUtc = u.FailedAttempts + 1 >= maxAttempts ? lockoutUntilUtc : u.LockoutUntilUtc,
        });

    public Task RecordSuccessAsync(Guid userId, DateTime nowUtc, CancellationToken ct) =>
        Change(userId, u => u with { FailedAttempts = 0, LockoutUntilUtc = null, LastLoginUtc = nowUtc });

    private Task<T> Read<T>(Func<List<UserRecord>, T> read)
    {
        lock (_gate)
            return Task.FromResult(read(_users));
    }

    private Task Change(Guid userId, Func<UserRecord, UserRecord> change)
    {
        lock (_gate)
        {
            var index = _users.FindIndex(u => u.UserId == userId);
            if (index < 0)
                return Task.CompletedTask;
            _users[index] = change(_users[index]);
            Save();
        }
        return Task.CompletedTask;
    }

    private void Save() => SecureFile.WriteAllText(_path, JsonSerializer.Serialize(_users, Json));
}
