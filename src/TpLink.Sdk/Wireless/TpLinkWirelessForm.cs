namespace TpLink.Sdk.Wireless;

/// <summary>
/// Confirmed live per docs/phase3-live-findings.md — form names and the request bodies that
/// pair with them. Note the IoT and Guest sections are each read via a single POST carrying
/// three `form` params, returning one flat response with prefixed keys, not three separate
/// calls.
/// </summary>
public static class TpLinkWirelessForm
{
    public const string Wireless2G = "admin/wireless?form=wireless_2g";
    public const string Wireless5G = "admin/wireless?form=wireless_5g";
    public const string Iot = "admin/wireless?form=iot_2g&form=iot_5g&form=iot_5g_2";
    public const string Guest = "admin/wireless?form=guest_2g&form=guest_5g&form=guest_2g5g";

    public const string Wireless2GReadOperation = "operation=read_spf";
    public const string Wireless5GReadOperation = "operation=read_spf";
    public const string IotReadOperation = "operation=read_spf";
    public const string GuestReadOperation = "operation=read";

    /// <summary>
    /// Confirmed live (user-captured DevTools traffic, not this session's own request) — the
    /// IoT *write* form omits `iot_5g_2` entirely, unlike the 3-form read above. Operation is
    /// `write_spf`, mirroring `read_spf` — the write op suffix tracks the read op suffix for
    /// this form family (guest reads via plain `read`, writes via plain `write`).
    /// </summary>
    public const string IotWrite = "admin/wireless?form=iot_2g&form=iot_5g";
    public const string IotWriteOperation = "operation=write_spf";
}
