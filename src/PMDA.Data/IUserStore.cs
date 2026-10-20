namespace PMDA.Data;

/// <summary>PMDA's own user accounts. Hashing and policy live in the API; this is storage only.</summary>
public interface IUserStore
{
    Task<int> CountAsync(CancellationToken ct);
    Task<int> ActiveAdminCountAsync(CancellationToken ct);
    Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken ct);
    Task<UserRecord?> GetAsync(Guid userId, CancellationToken ct);
    Task<UserRecord?> FindByNameAsync(string userName, CancellationToken ct);

    /// <returns>False when the user name is taken (names are unique regardless of case).</returns>
    Task<bool> CreateAsync(UserRecord user, CancellationToken ct);

    /// <summary>Updates profile, role and active flag; a role or active change also rotates the security stamp.</summary>
    Task UpdateAsync(Guid userId, string displayName, UserRole role, bool isActive, CancellationToken ct);

    /// <summary>Sets a new hash, rotates the security stamp (signing out other sessions) and clears lockout.</summary>
    Task SetPasswordAsync(Guid userId, string passwordHash, CancellationToken ct);

    /// <summary>Re-hash after a successful login with an outdated hash format; keeps sessions.</summary>
    Task UpgradeHashAsync(Guid userId, string passwordHash, CancellationToken ct);

    Task RecordFailureAsync(Guid userId, int maxAttempts, DateTime lockoutUntilUtc, CancellationToken ct);
    Task RecordSuccessAsync(Guid userId, DateTime nowUtc, CancellationToken ct);
}
