using PMDA.Data;

namespace PMDA.Api.Contracts;

public sealed record AuthStateDto(bool SetupRequired, bool SetupAllowed);

public sealed record LoginRequest(string UserName, string Password);

public sealed record SetupRequest(string UserName, string DisplayName, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CurrentUserDto(Guid Id, string UserName, string DisplayName, UserRole Role);

public sealed record UserDto(Guid Id, string UserName, string DisplayName, UserRole Role, bool IsActive, bool LockedOut, DateTime CreatedUtc, DateTime? LastLoginUtc)
{
    public static UserDto From(UserRecord u, DateTime nowUtc) =>
        new(u.UserId, u.UserName, u.DisplayName, u.Role, u.IsActive, u.LockoutUntilUtc > nowUtc, u.CreatedUtc, u.LastLoginUtc);
}

public sealed record CreateUserRequest(string UserName, string DisplayName, string Password, UserRole Role);

public sealed record UpdateUserRequest(string DisplayName, UserRole Role, bool IsActive);

public sealed record ResetPasswordRequest(string NewPassword);
