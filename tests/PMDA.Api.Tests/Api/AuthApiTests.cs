using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using PMDA.Api.Contracts;
using PMDA.Api.Tests.Support;
using PMDA.Data;
using static PMDA.Api.Tests.Support.PmdaApiFactory;
using static PMDA.Api.Tests.Support.TestFlights;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class AuthApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        await _api.EnsureUsersAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private Task<HttpResponseMessage> Login(HttpClient client, string user, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, password), Json);

    [Fact]
    public async Task Everything_needs_a_session()
    {
        var anonymous = _api.CreateClient();

        var flights = await anonymous.GetAsync("/api/flights");
        var me = await anonymous.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, flights.StatusCode);
        Assert.Equal("application/problem+json", flights.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        await using var hub = _api.CreateHubConnection();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => hub.StartAsync());
    }

    [Fact]
    public async Task Login_me_and_logout()
    {
        var client = _api.CreateClient();

        var login = await Login(client, AnalystUser, Password);
        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", Json);
        await client.PostAsync("/api/auth/logout", null);
        var after = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(AnalystUser, me!.UserName);
        Assert.Equal(UserRole.Analyst, me.Role);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Session_cookie_is_http_only_and_strict()
    {
        var response = await Login(_api.CreateClient(), AdminUser, Password);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pmda.session=", StringComparison.Ordinal)).ToLowerInvariant();

        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
    }

    [Theory]
    [InlineData(AdminUser, "wrong-password")]
    [InlineData("nobody", Password)]
    public async Task Wrong_credentials_get_the_same_answer(string user, string password)
    {
        var response = await Login(_api.CreateClient(), user, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("not correct", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_failures_lock_the_account_until_an_admin_resets_it()
    {
        var admin = await _api.CreateSignedInClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("lock-me", "Lock Me", "first-password", UserRole.Analyst), Json);
        var user = (await created.Content.ReadFromJsonAsync<UserDto>(Json))!;
        var client = _api.CreateClient();

        for (var i = 0; i < 5; i++)
            await Login(client, "lock-me", "nope-nope");
        var locked = await Login(client, "lock-me", "first-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);

        await admin.PostAsJsonAsync($"/api/users/{user.Id}/password", new ResetPasswordRequest("second-password"), Json);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, "lock-me", "second-password")).StatusCode);
    }

    [Fact]
    public async Task Analysts_cannot_delete_or_manage_users()
    {
        var flight = await StoreAsync(db.ConnectionString, [Nav(0, 1, 1, 1)]);
        var analyst = await _api.CreateSignedInClientAsync(UserRole.Analyst);

        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync("/api/flights")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.DeleteAsync($"/api/flights/{flight}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.DeleteAsync("/api/flights?confirm=DELETE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Admin_manages_users_and_a_deactivated_user_is_signed_out()
    {
        var admin = await _api.CreateSignedInClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("new.user", "New User", "a-good-password", UserRole.Analyst), Json);
        var duplicate = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("NEW.USER", "Again", "a-good-password", UserRole.Analyst), Json);
        var user = (await created.Content.ReadFromJsonAsync<UserDto>(Json))!;

        var session = _api.CreateClient();
        await Login(session, "new.user", "a-good-password");
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/flights")).StatusCode);

        var updated = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest("New User", UserRole.Analyst, false), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.False((await updated.Content.ReadFromJsonAsync<UserDto>(Json))!.IsActive);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/flights")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(_api.CreateClient(), "new.user", "a-good-password")).StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_demoted_and_nobody_deactivates_themselves()
    {
        var admin = await _api.CreateSignedInClientAsync();
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", Json))!;

        var demote = await admin.PutAsJsonAsync($"/api/users/{me.Id}", new UpdateUserRequest("x", UserRole.Analyst, true), Json);
        var deactivate = await admin.PutAsJsonAsync($"/api/users/{me.Id}", new UpdateUserRequest("x", UserRole.Admin, false), Json);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var admin = await _api.CreateSignedInClientAsync();

        var tooShort = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("short.pw", "x", "1234567", UserRole.Analyst), Json);
        var sameAsName = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("samename1", "x", "SameName1", UserRole.Analyst), Json);

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sameAsName.StatusCode);
    }

    [Fact]
    public async Task Users_change_their_own_password()
    {
        var analyst = await _api.CreateSignedInClientAsync(UserRole.Analyst);

        var wrong = await analyst.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest("nope", "brand-new-password"), Json);
        var ok = await analyst.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(Password, "brand-new-password"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync("/api/auth/me")).StatusCode); // this session continues
        Assert.Equal(HttpStatusCode.OK, (await Login(_api.CreateClient(), AnalystUser, "brand-new-password")).StatusCode);
    }

    [Fact]
    public async Task First_run_setup_only_without_users_and_only_where_allowed()
    {
        // A host with an empty users file: nobody has been created yet.
        await using var fresh = new PmdaApiFactory(db);
        var remote = await fresh.CreateClient().GetFromJsonAsync<AuthStateDto>("/api/auth/state", Json);
        var refused = await fresh.CreateClient().PostAsJsonAsync("/api/auth/setup", new SetupRequest("first.admin", "First", "a-good-password"), Json);
        Assert.True(remote!.SetupRequired);
        Assert.False(remote.SetupAllowed); // the test server is not a loopback connection
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        await using var local = new PmdaApiFactory(db);
        local.Settings["Auth:AllowRemoteSetup"] = "true";
        var client = local.CreateClient();
        var setup = await client.PostAsJsonAsync("/api/auth/setup", new SetupRequest("first.admin", "First Admin", "a-good-password"), Json);
        var again = await local.CreateClient().PostAsJsonAsync("/api/auth/setup", new SetupRequest("second", "Second", "a-good-password"), Json);

        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal(UserRole.Admin, (await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", Json))!.Role);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}
