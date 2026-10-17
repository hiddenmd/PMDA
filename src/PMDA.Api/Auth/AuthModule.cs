using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using PMDA.Data;

namespace PMDA.Api.Auth;

public static class AuthModule
{
    public static IServiceCollection AddPmdaAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.Section));
        services.AddSingleton<AuthService>();

        var sessionHours = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>()?.SessionHours ?? new AuthOptions().SessionHours;
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "pmda.session";
                o.Cookie.HttpOnly = true;
                // Strict: the cookie never rides on cross-site requests, which blocks CSRF.
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(sessionHours);
                o.SlidingExpiration = true;
                // An API answers 401/403; it never redirects to a login page.
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
            });

        services.AddAuthorizationBuilder()
            // Everything needs a signed-in user unless an endpoint explicitly allows anonymous access.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin)));
        return services;
    }

    /// <summary>
    /// Ends sessions of users who were deactivated, or whose role or password changed since the
    /// cookie was issued (the security stamp rotates on those changes).
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserRepository>();
        var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = ctx.Principal?.FindFirstValue(PmdaClaims.SecurityStamp);
        var user = Guid.TryParse(id, out var userId) ? await users.GetAsync(userId, ctx.HttpContext.RequestAborted) : null;
        if (user is null || !user.IsActive || user.SecurityStamp.ToString() != stamp)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
