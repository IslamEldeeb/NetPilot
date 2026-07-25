using System.Text.Json;
using TpLink.Sdk.Wireless;

namespace TpLink.Sdk.Tests;

/// <summary>
/// Replays the JSON shapes captured live in docs/phase3-live-findings.md. Field values below
/// are synthetic (fake SSIDs/PSKs) — the real household network's config is never committed
/// to source control, only the field names and on-wire conventions it confirmed.
/// </summary>
public class WirelessParsingTests
{
    private const string Wireless2GFixture = """
        {
          "success": true,
          "data": {
            "enable": "on",
            "ssid": "TestNet",
            "hidden": "off",
            "psk_key": "test-psk-0000",
            "encryption": "4",
            "channel": "6",
            "current_channel": "6",
            "txpower": "high",
            "htmode": "auto",
            "macaddr": "AC-A7-F1-02-9D-4B"
          }
        }
        """;

    private const string Wireless5GFixture = """
        {
          "success": true,
          "data": {
            "enable": "on",
            "ssid": "TestNet",
            "hidden": "off",
            "psk_key": "test-psk-0000",
            "encryption": "4",
            "channel": "auto",
            "current_channel": "36",
            "txpower": "high",
            "htmode": "160",
            "macaddr": "AC-A7-F1-02-9D-4A"
          }
        }
        """;

    private const string IotFixture = """
        {
          "success": true,
          "data": {
            "iot_2g_enable": "on",
            "iot_2g_ssid": "TestNet-IOT",
            "iot_2g_hidden": "off",
            "iot_2g_psk_key": "test-iot-psk-1",
            "iot_2g_encryption": "1",
            "iot_5g_enable": "off",
            "iot_5g_ssid": "TP-Link_IoT_TEST_5G",
            "iot_5g_hidden": "off",
            "iot_5g_psk_key": "test-iot-psk-2",
            "iot_5g_encryption": "1",
            "iot_5g_2_ssid": ""
          }
        }
        """;

    private const string GuestFixture = """
        {
          "success": true,
          "data": {
            "guest_2g_enable": "off",
            "guest_2g_ssid": "TP-Link_Guest_TEST",
            "guest_2g_hidden": "off",
            "guest_2g_psk_key": "test-guest-psk-1",
            "guest_2g_encryption": "psk",
            "guest_5g_enable": "off",
            "guest_5g_ssid": "TP-Link_Guest_TEST_5G",
            "guest_5g_hidden": "off",
            "guest_5g_psk_key": "test-guest-psk-2",
            "guest_5g_encryption": "psk"
          }
        }
        """;

    [Fact]
    public void MainBand_ParsesNumericChannel_2GFixture()
    {
        var result = JsonSerializer.Deserialize<TpLinkWirelessResponse<TpLinkMainBandConfig>>(Wireless2GFixture)!;

        Assert.True(result.Success);
        var data = result.Data!;
        Assert.True(data.IsEnabled);
        Assert.True(data.IsBroadcasting);
        Assert.Equal("TestNet", data.Ssid);
        Assert.Equal("6", data.Channel);
        Assert.Equal(6, data.CurrentChannel);
        Assert.Equal("high", data.TxPower);
    }

    [Fact]
    public void MainBand_ParsesAutoChannel_5GFixture()
    {
        var result = JsonSerializer.Deserialize<TpLinkWirelessResponse<TpLinkMainBandConfig>>(Wireless5GFixture)!;

        var data = result.Data!;
        Assert.Equal("auto", data.Channel);
        Assert.Equal(36, data.CurrentChannel);
        Assert.Equal("160", data.ChannelWidth);
    }

    [Fact]
    public void MainBand_Hidden_MeansNotBroadcasting()
    {
        var hiddenVariant = Wireless2GFixture.Replace("\"hidden\": \"off\"", "\"hidden\": \"on\"");
        var result = JsonSerializer.Deserialize<TpLinkWirelessResponse<TpLinkMainBandConfig>>(hiddenVariant)!;

        Assert.False(result.Data!.IsBroadcasting);
    }

    [Fact]
    public void Iot_ParsesBothBands_FromOneFlatObject()
    {
        var result = JsonSerializer.Deserialize<TpLinkWirelessResponse<TpLinkIotConfig>>(IotFixture)!;

        var data = result.Data!;
        Assert.True(data.Is2gEnabled);
        Assert.Equal("TestNet-IOT", data.Iot2gSsid);
        Assert.False(data.Is5gEnabled);
        Assert.Equal("TP-Link_IoT_TEST_5G", data.Iot5gSsid);
    }

    [Fact]
    public void Guest_ParsesBothBands_FromOneFlatObject()
    {
        var result = JsonSerializer.Deserialize<TpLinkWirelessResponse<TpLinkGuestConfig>>(GuestFixture)!;

        var data = result.Data!;
        Assert.False(data.Is2gEnabled);
        Assert.Equal("TP-Link_Guest_TEST", data.Guest2gSsid);
        Assert.False(data.Is5gEnabled);
        Assert.Equal("TP-Link_Guest_TEST_5G", data.Guest5gSsid);
    }

    [Fact]
    public void WirelessScheduleSettings_ParsesInforceFlag()
    {
        var json = """{"success":true,"data":{"inforce":false}}""";
        var result = JsonSerializer.Deserialize<TpLinkWirelessResponse<Dictionary<string, bool>>>(json)!;

        Assert.True(result.Success);
        Assert.False(result.Data!["inforce"]);
    }
}
