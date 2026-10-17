using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NetPilot.Abstractions;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Usage;
using NetPilot.Core.Settings;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Web;

namespace NetPilot.Data.Tests.Web;

public sealed class SettingsActionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-settings-actions-{Guid.NewGuid():N}");
    public SettingsActionTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task BlankPasswordPreservesCiphertext_AndConnectionTestDoesNotPersistDraft()
    {
        await using var services = new ServiceCollection().AddLogging().AddNetPilotData(Path.Combine(_directory, "test.db"), Path.Combine(_directory, "keys")).BuildServiceProvider();
        var provider = new FakeRouterProvider(new());
        var connections = services.GetRequiredService<IRouterConnectionStore>();
        var passwords = services.GetRequiredService<RouterPasswordProtector>();
        var actions = new SettingsRouterActions(provider, connections, passwords, new LiteActivityLogStore(services.GetRequiredService<NetPilotDatabase>()), new());
        await actions.SaveAsync("192.168.1.1", false, "router-secret");
        var first = (await connections.GetAsync(default))!;
        Assert.NotEqual("router-secret", first.EncryptedPassword);
        await actions.SaveAsync("router.lan", true, "");
        var saved = (await connections.GetAsync(default))!;
        Assert.Equal(first.EncryptedPassword, saved.EncryptedPassword); Assert.Equal("router-secret", passwords.Decrypt(saved.EncryptedPassword));
        await actions.TestAsync("unsaved.lan", false, "one-shot-test-secret");
        Assert.Equal(saved, await connections.GetAsync(default));
        Assert.Empty(provider.AppliedLimits); Assert.Empty(provider.AppliedWirelessUpdates);
    }

    [Fact]
    public async Task UnsupportedProviderCannotWriteWirelessEvenWithPerNetworkFlags()
    {
        await using var services = new ServiceCollection().AddLogging().AddNetPilotData(Path.Combine(_directory, "test.db"), Path.Combine(_directory, "keys")).BuildServiceProvider();
        var provider = new FakeRouterProvider(new() { SupportsWirelessToggle = true });
        var actions = new SettingsRouterActions(provider, services.GetRequiredService<IRouterConnectionStore>(), services.GetRequiredService<RouterPasswordProtector>(), new LiteActivityLogStore(services.GetRequiredService<NetPilotDatabase>()), new());
        var network = new WirelessNetworkState(new(WirelessNetworkKind.Guest, WirelessBand.Ghz24), "Guest", true, "Guest", true, "WPA2", null, null, null, new(true, true, true, false, false, false, false), []);
        await Assert.ThrowsAsync<SettingsValidationException>(() => actions.ApplyWirelessAsync(network, true, "Changed", ""));
        Assert.Empty(provider.AppliedWirelessUpdates);
    }

    [Fact]
    public async Task DownloadEndpointRejectsFormWithoutAntiforgery_BeforeOpeningBackup()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = _directory });
        builder.Configuration["NetPilot:Backup:DataDirectory"] = _directory;
        builder.Services.AddBackupEndpoints(builder.Configuration);
        builder.Services.AddAntiforgery();
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.WebHost.UseKestrel();
        await using var app = builder.Build();
        app.MapBackupEndpoints();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Single();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = "POST"; context.Request.ContentType = "application/x-www-form-urlencoded"; context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
