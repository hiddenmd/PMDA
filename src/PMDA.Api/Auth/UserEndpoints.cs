using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Contracts;
using PMDA.Data;

namespace PMDA.Api.Auth;

/// <summary>User administration (Admin only).</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(Policies.Admin);

        group.MapGet("/", List).WithName("ListUsers");
        group.MapPost("/", Create).WithName("CreateUser").ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{id:guid}", Update).WithName("UpdateUser")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/password", ResetPassword).WithName("ResetUserPassword")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Ok<UserDto[]>> List(IUserStore users, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return TypedResults.Ok((await users.ListAsync(ct)).Select(u => UserDto.From(u, now)).ToArray());
    }

    private static async Task<Results<Created<UserDto>, ValidationProblem, ProblemHttpResult>> Create(
        CreateUserRequest request, IUserStore users, AuthService auth, TimeProvider time, CancellationToken ct)
    {
        if (AuthEndpoints.Validate(request.UserName, request.Password, auth) is { } problem)
            return problem;
        var user = auth.NewUser(request.UserName, request.DisplayName ?? "", request.Password, request.Role);
        if (!await users.CreateAsync(user, ct))
            return TypedResults.Problem($"The user name '{user.UserName}' is taken.", statusCode: StatusCodes.Status409Conflict);
        return TypedResults.Created($"/api/users/{user.UserId}", UserDto.From(user, time.GetUtcNow().UtcDateTime));
    }

    private static async Task<Results<Ok<UserDto>, NotFound, ValidationProblem, ProblemHttpResult>> Update(
        Guid id, UpdateUserRequest request, ClaimsPrincipal me, IUserStore users, TimeProvider time, CancellationToken ct)
    {
        var user = await users.GetAsync(id, ct);
        if (user is null)
            return TypedResults.NotFound();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 128)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["Use 1 to 128 characters."] });

        var isSelf = me.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString();
        if (isSelf && !request.IsActive)
            return TypedResults.Problem("You cannot deactivate your own account.", statusCode: StatusCodes.Status409Conflict);
        var losesAdmin = user is { Role: UserRole.Admin, IsActive: true } && (request.Role != UserRole.Admin || !request.IsActive);
        if (losesAdmin && await users.ActiveAdminCountAsync(ct) <= 1)
            return TypedResults.Problem("PMDA needs at least one active administrator.", statusCode: StatusCodes.Status409Conflict);

        await users.UpdateAsync(id, request.DisplayName.Trim(), request.Role, request.IsActive, ct);
        return TypedResults.Ok(UserDto.From((await users.GetAsync(id, ct))!, time.GetUtcNow().UtcDateTime));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPassword(
        Guid id, ResetPasswordRequest request, IUserStore users, AuthService auth, CancellationToken ct)
    {
        var user = await users.GetAsync(id, ct);
        if (user is null)
            return TypedResults.NotFound();
        if (auth.CheckPassword(request.NewPassword ?? "", user.UserName) is { } error)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = [error] });

        // Also clears lockout and signs the user out everywhere (security stamp rotates).
        await users.SetPasswordAsync(id, auth.Hash(user, request.NewPassword!), ct);
        return TypedResults.NoContent();
    }
}
