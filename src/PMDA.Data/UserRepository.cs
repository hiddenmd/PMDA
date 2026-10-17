using Dapper;
using Microsoft.Data.SqlClient;

namespace PMDA.Data;

public enum UserRole : byte
{
    Analyst = 0,
    Admin = 1,
}

public sealed record UserRecord(
    Guid UserId,
    string UserName,
    string DisplayName,
    string PasswordHash,
    UserRole Role,
    bool IsActive,
    int FailedAttempts,
    DateTime? LockoutUntilUtc,
    Guid SecurityStamp,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc);

/// <summary>PMDA's own user accounts. Hashing and policy live in the API; this is storage only.</summary>
public sealed class UserRepository(string connectionString)
{
    private const string Columns = """
        UserId, UserName, DisplayName, PasswordHash, Role, IsActive, FailedAttempts, LockoutUntilUtc,
        SecurityStamp, CreatedUtc, LastLoginUtc
        """;

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.AppUser;", cancellationToken: ct));
    }

    public async Task<int> ActiveAdminCountAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.AppUser WHERE Role = @Admin AND IsActive = 1;", new { Admin = (byte)UserRole.Admin }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        var rows = await conn.QueryAsync<UserRecord>(new CommandDefinition($"SELECT {Columns} FROM dbo.AppUser ORDER BY UserName;", cancellationToken: ct));
        return rows.Select(AsUtc).ToList();
    }

    public async Task<UserRecord?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        var user = await conn.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.AppUser WHERE UserId = @userId;", new { userId }, cancellationToken: ct));
        return user is null ? null : AsUtc(user);
    }

    public async Task<UserRecord?> FindByNameAsync(string userName, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        var user = await conn.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition(
            $"SELECT {Columns} FROM dbo.AppUser WHERE UserName = @userName;", new { userName }, cancellationToken: ct));
        return user is null ? null : AsUtc(user);
    }

    /// <returns>False when the user name is taken.</returns>
    public async Task<bool> CreateAsync(UserRecord user, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT dbo.AppUser (UserId, UserName, DisplayName, PasswordHash, Role, IsActive, SecurityStamp)
                VALUES (@UserId, @UserName, @DisplayName, @PasswordHash, @Role, @IsActive, @SecurityStamp);
                """,
                new { user.UserId, user.UserName, user.DisplayName, user.PasswordHash, Role = (byte)user.Role, user.IsActive, user.SecurityStamp },
                cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    /// <summary>Updates profile, role and active flag; a role or active change also rotates the security stamp.</summary>
    public async Task UpdateAsync(Guid userId, string displayName, UserRole role, bool isActive, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.AppUser
            SET SecurityStamp = CASE WHEN Role <> @Role OR IsActive <> @IsActive THEN NEWID() ELSE SecurityStamp END,
                DisplayName = @DisplayName, Role = @Role, IsActive = @IsActive
            WHERE UserId = @userId;
            """, new { userId, displayName, Role = (byte)role, isActive }, cancellationToken: ct));
    }

    /// <summary>Sets a new hash, rotates the security stamp (signing out other sessions) and clears lockout.</summary>
    public async Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.AppUser
            SET PasswordHash = @passwordHash, SecurityStamp = NEWID(), FailedAttempts = 0, LockoutUntilUtc = NULL
            WHERE UserId = @userId;
            """, new { userId, passwordHash }, cancellationToken: ct));
    }

    /// <summary>Re-hash after a successful login with an outdated hash format; keeps sessions.</summary>
    public async Task UpgradeHashAsync(Guid userId, string passwordHash, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.AppUser SET PasswordHash = @passwordHash WHERE UserId = @userId;", new { userId, passwordHash }, cancellationToken: ct));
    }

    public async Task RecordFailureAsync(Guid userId, int maxAttempts, DateTime lockoutUntilUtc, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.AppUser
            SET FailedAttempts = FailedAttempts + 1,
                LockoutUntilUtc = CASE WHEN FailedAttempts + 1 >= @maxAttempts THEN @lockoutUntilUtc ELSE LockoutUntilUtc END
            WHERE UserId = @userId;
            """, new { userId, maxAttempts, lockoutUntilUtc }, cancellationToken: ct));
    }

    public async Task RecordSuccessAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.AppUser SET FailedAttempts = 0, LockoutUntilUtc = NULL, LastLoginUtc = @nowUtc WHERE UserId = @userId;
            """, new { userId, nowUtc }, cancellationToken: ct));
    }

    private static UserRecord AsUtc(UserRecord u) => u with
    {
        LockoutUntilUtc = u.LockoutUntilUtc is { } l ? DateTime.SpecifyKind(l, DateTimeKind.Utc) : null,
        CreatedUtc = DateTime.SpecifyKind(u.CreatedUtc, DateTimeKind.Utc),
        LastLoginUtc = u.LastLoginUtc is { } x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null,
    };
}
