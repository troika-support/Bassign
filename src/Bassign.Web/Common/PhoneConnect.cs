using Microsoft.AspNetCore.Components;
using QRCoder;

namespace Bassign.Web.Common;

/// <summary>
/// How a phone finds this organization's server (decision #16). The QR code and the shared link are the same
/// https page — <c>/connect</c> on this website — so a phone's ordinary camera can scan it. That page opens the
/// app through the <c>bassign://connect?server=…</c> link (or the Play Store if the app isn't installed); the
/// app then reads <c>/connect/info</c> here to learn the organization's name and API address, and asks the
/// person to confirm both before they sign in.
/// </summary>
public static class PhoneConnect
{
    public const int Protocol = 1;

    public static string PageUrl(string baseUri) => baseUri.TrimEnd('/') + "/connect";

    /// <summary>bassign://connect?server=tasks.client.co.zw — host only; the app always uses https.</summary>
    public static string AppLink(string baseUri) => $"bassign://connect?server={Uri.EscapeDataString(new Uri(baseUri).Authority)}";

    /// <summary>Android Chrome "intent:" link: opens the app if installed, otherwise the fallback (the Play Store).</summary>
    public static string AndroidIntent(string baseUri, string storeUrl) =>
        $"intent://connect?server={Uri.EscapeDataString(new Uri(baseUri).Authority)}#Intent;scheme=bassign;package={Bassign.Infrastructure.Options.PublicOptions.AndroidAppId};" +
        $"S.browser_fallback_url={Uri.EscapeDataString(storeUrl)};end";

    /// <summary>An inline SVG QR code (dark on white, quiet zone kept so every camera reads it).</summary>
    public static MarkupString QrSvg(string text, int pixelsPerModule = 6)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var svg = new SvgQRCode(data);
        return new MarkupString(svg.GetGraphic(pixelsPerModule, "#000000", "#FFFFFF", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute));
    }
}

/// <summary>What the phone app reads from /connect/info.</summary>
public sealed record ConnectInfo(string OrganizationName, string ApiBaseUrl, int Protocol);
