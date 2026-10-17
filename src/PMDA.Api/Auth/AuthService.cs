using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PMDA.Data;

namespace PMDA.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int SessionHours { get; set; } = 8;
    public int MinPasswordLength { get; set; } = 8;

    /// <summary>
    /// First-run setup is only accepted from the server machine itself, so nobody on the network can
    /// claim the administrator account first. Only automated test hosts turn this on.
    /// </summary>
    public bool AllowRemoteSetup { get; set; }
}

public static class PmdaClaims
{
    public const string DisplayName = "pmda:display_name";
    public const string SecurityStamp = "pmda:stamp";
}

public static class Policies
{
    public const string Admin = "Admin";
}

public enum LoginOutcome { Success, Invalid, LockedOut }

public sealed record LoginResult(LoginOutcome Outcome, UserRecord? User = null);

/// <summary>Credential checks, lockout and password policy.</summary>
public sealed class AuthService(UserRepository users, IOptions<AuthOptions> options, TimeProvider time)
{
    private readonly PasswordHasher<UserRecord> _hasher = new();
    private AuthOptions Options => options.Value;
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(userName.Trim(), ct);
        if (user is null || !user.IsActive)
        {
            // Same work and answer as a wrong password: no hint whether the account exists.
            _hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            return new LoginResult(LoginOutcome.Invalid);
        }
        if (user.LockoutUntilUtc > Now)
            return new LoginResult(LoginOutcome.LockedOut);

        var verdict = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed)
        {
            await users.RecordFailureAsync(user.UserId, Options.MaxFailedAttempts, Now.AddMinutes(Options.LockoutMinutes), ct);
            return new LoginResult(user.FailedAttempts + 1 >= Options.MaxFailedAttempts ? LoginOutcome.LockedOut : LoginOutcome.Invalid);
        }
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            await users.UpgradeHashAsync(user.UserId, _hasher.HashPassword(user, password), ct);

        await users.RecordSuccessAsync(user.UserId, Now, ct);
        return new LoginResult(LoginOutcome.Success, user);
    }

    public bool VerifyPassword(UserRecord user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    public string Hash(UserRecord user, string password) => _hasher.HashPassword(user, password);

    /// <returns>A message saying what is wrong, or null when the password is acceptable.</returns>
    public string? CheckPassword(string password, string userName)
    {
        if (password.Length < Options.MinPasswordLength)
            return $"Use at least {Options.MinPasswordLength} characters.";
        if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
            return "The password must differ from the user name.";
        return null;
    }

    /// <returns>A message saying what is wrong, or null when the user name is acceptable.</returns>
    public static string? CheckUserName(string userName)
    {
        var name = userName.Trim();
        if (name.Length is < 2 or > 64)
            return "Use 2 to 64 characters.";
        if (!name.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            return "Use letters, digits, '.', '-' or '_'.";
        return null;
    }

    public UserRecord NewUser(string userName, string displayName, string password, UserRole role)
    {
        var user = new UserRecord(Guid.NewGuid(), userName.Trim(), string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
            "", role, true, 0, null, Guid.NewGuid(), Now, null);
        return user with { PasswordHash = Hash(user, password) };
    }

    public ClaimsPrincipal Principal(UserRecord user, string scheme) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.UserName),
        new Claim(PmdaClaims.DisplayName, user.DisplayName),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim(PmdaClaims.SecurityStamp, user.SecurityStamp.ToString()),
    ], scheme));

    private static readonly UserRecord DummyUser = new(Guid.Empty, "", "", "", UserRole.Analyst, false, 0, null, Guid.Empty, DateTime.MinValue, null);
    private static readonly string DummyHash = new PasswordHasher<UserRecord>().HashPassword(DummyUser, "not-a-real-password");
}
