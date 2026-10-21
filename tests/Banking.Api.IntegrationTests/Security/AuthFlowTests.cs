using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Security;

/// <summary>A2-04: every branch of the login flow, sessions, refresh, logout, forced logout, idle sweep.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuthFlowTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;

    public AuthFlowTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _host = await ApiTestHost.CreateAsync(_fixture, "auth");

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Wrong_secret_unknown_code_and_disabled_user_get_the_same_401_and_are_logged()
    {
        await _host.CreateStaffAsync("DISABLED1", canSwitchBranch: false);
        await using (var context = _host.NewContext())
        {
            var user = await context.Set<StaffUser>().SingleAsync(row => row.StaffCode == "DISABLED1");
            user.IsDisabled = true;
            await context.SaveChangesAsync();
        }

        foreach (var (code, secret) in new[] { ("ADMIN", "wrong"), ("NOBODY", "whatever"), ("DISABLED1", ApiTestHost.StaffSecret) })
        {
            using var response = await _host.LoginAsync(code, secret);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(ProblemCodes.InvalidCredentials);
        }

        await using var check = _host.NewContext();
        var failedId = await check.Set<EventType>().Where(row => row.EventCode == EventCodes.LoginFailed).Select(row => row.EventTypeId).SingleAsync();
        var attempted = await check.Set<UserActivityLog>()
            .Where(row => row.EventTypeId == failedId)
            .Select(row => row.EntityId)
            .ToListAsync();
        attempted.ShouldBe(["ADMIN", "NOBODY", "DISABLED1"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_and_disabled_codes_still_check_one_password_hash_so_timing_reveals_nothing()
    {
        await _host.CreateStaffAsync("DISABLED2", canSwitchBranch: false);
        await using (var context = _host.NewContext())
        {
            var user = await context.Set<StaffUser>().SingleAsync(row => row.StaffCode == "DISABLED2");
            user.IsDisabled = true;
            await context.SaveChangesAsync();
        }

        var hasher = new CountingHasher();
        await using var factory = _host.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IPasswordHasher<StaffUser>>(hasher)));
        using var client = factory.CreateClient();

        foreach (var code in new[] { "NOBODY2", "DISABLED2", "ADMIN" })
        {
            hasher.Reset();
            using var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { staffCode = code, secretKey = "Wrong-Secret-1" });
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            hasher.Verifications.ShouldBe(1, $"one hash check for {code}");
        }
    }

    [Fact]
    public async Task Bootstrap_admin_must_change_the_secret_key_before_anything_else()
    {
        using var login = await _host.LoginAsync(ApiTestHost.AdminCode, ApiTestHost.AdminSecret);
        var body = await ApiTestHost.ReadJsonAsync(login);
        body.GetProperty("mustChangeSecretKey").GetBoolean().ShouldBeTrue();
        var token = body.GetProperty("accessToken").GetString()!;

        using var client = _host.Client(token);
        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var blocked = await client.GetAsync(new Uri("/api/v1/sessions", UriKind.Relative));
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiTestHost.ProblemTypeAsync(blocked)).ShouldBe(ProblemCodes.MustChangeSecretKey);

        using var weak = await client.PostAsJsonAsync(
            "/api/v1/auth/change-secret-key",
            new { currentSecretKey = ApiTestHost.AdminSecret, newSecretKey = "short" });
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiTestHost.ProblemTypeAsync(weak)).ShouldBe(ProblemCodes.SecretKeyRules);

        using var changed = await client.PostAsJsonAsync(
            "/api/v1/auth/change-secret-key",
            new { currentSecretKey = ApiTestHost.AdminSecret, newSecretKey = "Admin-Pass2" });
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newToken = (await ApiTestHost.ReadJsonAsync(changed)).GetProperty("accessToken").GetString()!;

        using var admin = _host.Client(newToken);
        (await admin.GetAsync(new Uri("/api/v1/sessions", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var check = _host.NewContext();
        var hash = await check.Set<StaffUser>().Where(row => row.StaffCode == "ADMIN").Select(row => row.SecretKeyHash).SingleAsync();
        var audit = await check.Set<DataChangeAuditLog>()
            .Where(row => row.TableName == nameof(StaffUser) && row.ActionType == "UPDATE")
            .Select(row => row.NewValueJson)
            .ToListAsync();
        audit.ShouldNotBeEmpty();
        audit.ShouldAllBe(json => !json!.Contains(hash!));
    }

    [Fact]
    public async Task Five_failures_lock_the_code_even_with_the_right_secret()
    {
        await _host.CreateStaffAsync("LOCKME", canSwitchBranch: false);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await _host.LoginAsync("LOCKME", "Wrong-Pass1");
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var locked = await _host.LoginAsync("LOCKME", ApiTestHost.StaffSecret);
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await ApiTestHost.ReadJsonAsync(locked);
        problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.Locked);
        problem.GetProperty("retryAfterSeconds").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_new_login_supersedes_the_previous_session()
    {
        await _host.CreateStaffAsync("TWICE", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        var first = await _host.TokenAsync("TWICE");
        var second = await _host.TokenAsync("TWICE");

        using var old = await _host.Client(first).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiTestHost.ProblemTypeAsync(old)).ShouldBe(ProblemCodes.SessionSuperseded);

        (await _host.Client(second).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_issues_a_working_token_and_logout_ends_the_session()
    {
        await _host.CreateStaffAsync("CYCLE", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        var token = await _host.TokenAsync("CYCLE");

        using var refreshed = await _host.Client(token).PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newToken = (await ApiTestHost.ReadJsonAsync(refreshed)).GetProperty("accessToken").GetString()!;

        using var client = _host.Client(newToken);
        (await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiTestHost.ProblemTypeAsync(after)).ShouldBe(ProblemCodes.SessionEnded);
    }

    [Fact]
    public async Task Forced_logout_takes_effect_on_the_next_request()
    {
        await _host.CreateStaffAsync("FORCED", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        var tellerToken = await _host.TokenAsync("FORCED");
        var adminToken = await _host.AdminTokenAsync();

        long sessionId;
        await using (var context = _host.NewContext())
        {
            sessionId = await context.Set<UserSession>()
                .Where(row => row.StaffUser!.StaffCode == "FORCED" && row.LogoutAt == null)
                .Select(row => row.SessionId)
                .SingleAsync();
        }

        using var teller = _host.Client(tellerToken);
        (await teller.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var ended = await _host.Client(adminToken).PostAsync(new Uri($"/api/v1/sessions/{sessionId}/end", UriKind.Relative), null);
        ended.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await teller.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiTestHost.ProblemTypeAsync(after)).ShouldBe(ProblemCodes.SessionForced);
    }

    [Fact]
    public async Task Sweeper_closes_idle_sessions()
    {
        await _host.CreateStaffAsync("IDLE", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        var token = await _host.TokenAsync("IDLE");

        // The login row is the session's only activity and is fresh, so make the clock run ahead instead.
        using var scope = _host.Factory.Services.CreateScope();
        var admin = ActivatorUtilities.CreateInstance<SessionAdmin>(scope.ServiceProvider, new AheadTimeProvider(TimeSpan.FromMinutes(20)));
        (await admin.SweepAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);

        using var after = await _host.Client(token).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiTestHost.ProblemTypeAsync(after)).ShouldBe(ProblemCodes.SessionTimedOut);

        await using var check = _host.NewContext();
        var reason = await check.Set<UserSession>()
            .Where(row => row.StaffUser!.StaffCode == "IDLE")
            .Select(row => row.LogoutReason!.Code)
            .SingleAsync();
        reason.ShouldBe(LookupCodes.LogoutReasons.IdleTimeout);
    }

    private sealed class AheadTimeProvider(TimeSpan ahead) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + ahead;
    }

    /// <summary>The real hasher, counting verifications.</summary>
    private sealed class CountingHasher : IPasswordHasher<StaffUser>
    {
        private readonly PasswordHasher<StaffUser> _inner = new();
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public void Reset() => Volatile.Write(ref _verifications, 0);

        public string HashPassword(StaffUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(StaffUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
