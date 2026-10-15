using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class QuotaActionTokenServiceTests
{
    private static QuotaActionClaim Claim => new(new("aa:bb:cc:dd:ee:01"), QuotaPeriodKind.Day, "2026-10-15", NotificationActionIds.Extend1Gb);

    [Fact]
    public void RoundTripBindsAllClaims_AndEveryMintIsFresh()
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = new QuotaActionTokenService(provider, new(), new());
        var token = service.Create(Claim);
        var result = new QuotaActionTokenService(provider, new(), new()).Validate(token);
        Assert.Equal(QuotaActionStatus.Ok, result.Status);
        Assert.Equal(Claim, result.Claim);
        Assert.NotEqual(token, service.Create(Claim));
        Assert.DoesNotContain("AA-BB-CC", token);
        // Two nested protected tokens must still fit in one encrypted Web Push payload.
        Assert.True(token.Length * 2 + 600 < 3900);
    }

    [Fact]
    public void TamperingWrongKeyAndMalformedInputs_ReturnInvalidWithoutThrowing()
    {
        var service = new QuotaActionTokenService(new EphemeralDataProtectionProvider(), new(), new());
        var token = service.Create(Claim);
        var position = token.Length / 2;
        var tampered = token[..position] + (token[position] == 'A' ? 'B' : 'A') + token[(position + 1)..];
        foreach (var invalid in new[] { null, "", "malformed", new string('x', 4097), tampered, token + "=" })
            Assert.Equal(QuotaActionStatus.Invalid, service.Validate(invalid).Status);
        var otherKeyRing = new QuotaActionTokenService(new EphemeralDataProtectionProvider(), new(), new());
        Assert.Equal(QuotaActionStatus.Invalid, otherKeyRing.Validate(token).Status);
    }

    [Fact]
    public void ConfiguredLifetimeExpiresAtBoundary_UsingClockWithoutSleeping()
    {
        var time = new MutableClock { Now = DateTimeOffset.UtcNow };
        var service = new QuotaActionTokenService(new EphemeralDataProtectionProvider(),
            new() { ActionTokenHours = 2 }, new(timeProvider: time));
        var token = service.Create(Claim);
        time.Now = time.Now.AddHours(2).AddMilliseconds(-1);
        Assert.Equal(QuotaActionStatus.Ok, service.Validate(token).Status);
        time.Now = time.Now.AddMilliseconds(1);
        Assert.Equal(QuotaActionStatus.Expired, service.Validate(token).Status);
    }

    [Fact]
    public async Task SharedKeyRingAndDatabaseAcceptOneConcurrentRedemptionAcrossTwoHosts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-action-hosts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var agent = HostServices(directory);
            using var web = HostServices(directory);
            var clock = agent.GetRequiredService<UsagePeriodClock>();
            var state = new QuotaPeriodState { Mac = Claim.Mac, PeriodKind = QuotaPeriodKind.Day, PeriodKey = clock.TodayKey() };
            await agent.GetRequiredService<IDeviceStore>().UpsertAsync(new Device { Mac = state.Mac }, CancellationToken.None);
            await agent.GetRequiredService<IQuotaStateStore>().UpsertAsync(state, CancellationToken.None);
            var token = agent.GetRequiredService<IQuotaActionTokenService>().Create(Claim with { PeriodKey = state.PeriodKey });
            var results = await Task.WhenAll(new[] { agent, web }.Select(host =>
                Task.Run(() => host.GetRequiredService<QuotaActionHandler>().ApplyAsync(token))));
            Assert.Single(results, result => result.Status == QuotaActionStatus.Ok);
            Assert.Single(results, result => result.Status == QuotaActionStatus.AlreadyUsed);
            var saved = (await web.GetRequiredService<IQuotaStateStore>().FindAsync(state.Id, CancellationToken.None))!;
            Assert.Equal(1_000_000_000, saved.ExtraAllowanceBytes);
            Assert.Single(saved.UsedActionTokenHashes);
            Assert.Single(await web.GetRequiredService<IActivityLogStore>().GetRecentAsync(20, CancellationToken.None));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ServiceProvider HostServices(string directory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new UsagePeriodClock());
        services.AddSingleton(new NotificationOptions());
        services.AddNetPilotData(Path.Combine(directory, "shared.db"), Path.Combine(directory, "keys"));
        return services.BuildServiceProvider();
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
