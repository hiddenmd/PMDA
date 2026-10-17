using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PMDA.Api.Contracts;
using PMDA.Data;

namespace PMDA.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapGet("/state", State).WithName("GetAuthState").AllowAnonymous()
            .WithSummary("Whether first-run setup is needed and allowed from this machine.");
        auth.MapPost("/setup", Setup).WithName("SetupAdministrator").AllowAnonymous()
            .WithSummary("Create the first administrator. Only while no users exist, only from the server machine.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapPost("/login", Login).WithName("Login").AllowAnonymous()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/logout", (Delegate)Logout).WithName("Logout").AllowAnonymous();
        auth.MapGet("/me", Me).WithName("GetCurrentUser").ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/password", ChangePassword).WithName("ChangePassword")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        return app;
    }

    private static async Task<Ok<AuthStateDto>> State(HttpContext http, UserRepository users, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var required = await users.CountAsync(ct) == 0;
        return TypedResults.Ok(new AuthStateDto(required, required && SetupAllowedFrom(http, options.Value)));
    }

    private static async Task<Results<Ok<CurrentUserDto>, ValidationProblem, ProblemHttpResult>> Setup(
        SetupRequest request, HttpContext http, UserRepository users, AuthService auth, IOptions<AuthOptions> options, CancellationToken ct)
    {
        if (!SetupAllowedFrom(http, options.Value))
            return TypedResults.Problem("Create the first administrator from the server machine itself.", statusCode: StatusCodes.Status403Forbidden);
        if (await users.CountAsync(ct) > 0)
            return TypedResults.Problem("PMDA is already set up. Sign in instead.", statusCode: StatusCodes.Status409Conflict);
        if (Validate(request.UserName, request.Password, auth) is { } problem)
            return problem;

        var admin = auth.NewUser(request.UserName, request.DisplayName, request.Password, UserRole.Admin);
        if (!await users.CreateAsync(admin, ct))
            return TypedResults.Problem("PMDA is already set up. Sign in instead.", statusCode: StatusCodes.Status409Conflict);
        await SignInAsync(http, auth, admin);
        return TypedResults.Ok(Current(admin));
    }

    private static async Task<Results<Ok<CurrentUserDto>, ProblemHttpResult>> Login(LoginRequest request, HttpContext http, AuthService auth, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.UserName ?? "", request.Password ?? "", ct);
        return result.Outcome switch
        {
            LoginOutcome.Success => await SignedIn(),
            LoginOutcome.LockedOut => TypedResults.Problem("Too many failed attempts. Try again later or ask an administrator.", statusCode: StatusCodes.Status429TooManyRequests),
            _ => TypedResults.Problem("The user name or password is not correct.", statusCode: StatusCodes.Status401Unauthorized),
        };

        async Task<Results<Ok<CurrentUserDto>, ProblemHttpResult>> SignedIn()
        {
            await SignInAsync(http, auth, result.User!);
            return TypedResults.Ok(Current(result.User!));
        }
    }

    private static async Task<NoContent> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static Ok<CurrentUserDto> Me(ClaimsPrincipal user) => TypedResults.Ok(new CurrentUserDto(
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        user.Identity!.Name!,
        user.FindFirstValue(PmdaClaims.DisplayName) ?? user.Identity.Name!,
        Enum.Parse<UserRole>(user.FindFirstValue(ClaimTypes.Role)!)));

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePassword(
        ChangePasswordRequest request, ClaimsPrincipal principal, HttpContext http, UserRepository users, AuthService auth, CancellationToken ct)
    {
        var user = await users.GetAsync(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);
        if (user is null)
            return TypedResults.Unauthorized();
        if (!auth.VerifyPassword(user, request.CurrentPassword ?? ""))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["The current password is not correct."] });
        if (auth.CheckPassword(request.NewPassword ?? "", user.UserName) is { } error)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = [error] });

        await users.SetPasswordAsync(user.UserId, auth.Hash(user, request.NewPassword!), ct);
        // The stamp rotated: re-issue this session's cookie, other sessions end.
        await SignInAsync(http, auth, (await users.GetAsync(user.UserId, ct))!);
        return TypedResults.NoContent();
    }

    internal static ValidationProblem? Validate(string userName, string password, AuthService auth)
    {
        var errors = new Dictionary<string, string[]>();
        if (AuthService.CheckUserName(userName ?? "") is { } nameError)
            errors["userName"] = [nameError];
        if (auth.CheckPassword(password ?? "", userName ?? "") is { } passwordError)
            errors["password"] = [passwordError];
        return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : null;
    }

    private static Task SignInAsync(HttpContext http, AuthService auth, UserRecord user) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, auth.Principal(user, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = false });

    private static CurrentUserDto Current(UserRecord u) => new(u.UserId, u.UserName, u.DisplayName, u.Role);

    private static bool SetupAllowedFrom(HttpContext http, AuthOptions options) =>
        options.AllowRemoteSetup || (http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip));
}
