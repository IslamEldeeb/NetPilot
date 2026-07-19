using System.Text.Json;
using TpLink.Sdk.Models;

namespace TpLink.Sdk.Tests;

/// <summary>Replays the exact JSON shapes captured live in docs/phase1-live-findings.md.</summary>
public class ModelParsingTests
{
    private const string LoadDeviceFixture = """
        {
          "success": true,
          "data": [
            {
              "index": 0,
              "key": "k1",
              "mac": "AA-BB-CC-DD-B3-D1",
              "ip": "192.168.1.50",
              "host": "Galaxy-S9",
              "deviceName": "C110",
              "deviceType": "IP Camera",
              "deviceTag": "iot_2.4G",
              "isGuest": false,
              "enableLimit": "on",
              "downloadLimit": "5120",
              "uploadLimit": "2048",
              "enablePriority": false,
              "speedLimitOnline": true,
              "timePeriod": -1,
              "remainTime": -1,
              "uploadSpeed": 0,
              "downloadSpeed": 0,
              "txrate": null,
              "rxrate": null,
              "onlineTime": "100",
              "trafficUsage": "200",
              "signal": null
            }
          ]
        }
        """;

    [Fact]
    public void LoadDeviceResponse_ParsesCameraFixture_WithStringKbpsFields()
    {
        var result = JsonSerializer.Deserialize<TpLinkLoadDeviceResponse>(LoadDeviceFixture)!;

        Assert.True(result.Success);
        var device = Assert.Single(result.Data);
        Assert.Equal("IP Camera", device.DeviceType);
        Assert.Equal(5120, device.DownloadLimit);
        Assert.Equal(2048, device.UploadLimit);
        Assert.True(device.IsLimitEnabled);
        Assert.True(device.IsOnline);
    }

    [Fact]
    public void LoadDeviceResponse_ParsesNumericKbpsFields_LeniencyConfirmedLive()
    {
        var numericVariant = LoadDeviceFixture
            .Replace("\"downloadLimit\": \"5120\"", "\"downloadLimit\": 5120")
            .Replace("\"uploadLimit\": \"2048\"", "\"uploadLimit\": 2048");

        var result = JsonSerializer.Deserialize<TpLinkLoadDeviceResponse>(numericVariant)!;

        Assert.Equal(5120, result.Data[0].DownloadLimit);
        Assert.Equal(2048, result.Data[0].UploadLimit);
    }

    [Fact]
    public void DeviceTag_Offline_MarksDeviceOffline()
    {
        var offlineVariant = LoadDeviceFixture.Replace("\"deviceTag\": \"iot_2.4G\"", "\"deviceTag\": \"offline\"");
        var result = JsonSerializer.Deserialize<TpLinkLoadDeviceResponse>(offlineVariant)!;

        Assert.False(result.Data[0].IsOnline);
    }

    [Fact]
    public void WriteResponse_ParsesMinimalSuccessBody()
    {
        var result = JsonSerializer.Deserialize<TpLinkWriteResponse>("""{"success": true}""")!;
        Assert.True(result.Success);
    }

    [Fact]
    public void MaxValuesResponse_ParsesConfirmedShape()
    {
        var json = """{"success":true,"data":{"max_rules": 16, "downloadLimitMax": 1000000, "uploadLimitMax": 1000000}}""";
        var result = JsonSerializer.Deserialize<TpLinkMaxValuesResponse>(json)!;

        Assert.True(result.Success);
        Assert.Equal(16, result.Data!.MaxRules);
    }

    [Fact]
    public void PasswordKeyResponse_ParsesReferenceClientShape()
    {
        var json = """{"success":true,"data":{"password":["deadbeef","010001"]}}""";
        var result = JsonSerializer.Deserialize<TpLinkPasswordKeyResponse>(json)!;

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Password.Count);
        Assert.Equal("deadbeef", result.Data.Password[0]);
    }

    [Fact]
    public void LoginResponse_ParsesStok_ConfirmedShape()
    {
        var json = """{"success":true,"data":{"stok":"0123456789abcdef0123456789abcdef"}}""";
        var result = JsonSerializer.Deserialize<TpLinkLoginResponse>(json)!;

        Assert.Equal(32, result.Data!.Stok.Length);
    }
}
