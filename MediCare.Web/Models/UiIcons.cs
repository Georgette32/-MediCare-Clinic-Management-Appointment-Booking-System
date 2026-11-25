using Microsoft.AspNetCore.Html;

namespace MediCare.Web.Models;

/// <summary>
/// The MediCare icon set (20px grid, 1.75px round stroke, currentColor), rendered as inline SVG so it needs no
/// font or image request and stays inside the Content-Security-Policy. Paths match the design system's icons.
/// </summary>
public static class UiIcons
{
    private static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["check"] = "M4.5 10.5l3.5 3.5 7.5-8",
        ["x"] = "M5.5 5.5l9 9M14.5 5.5l-9 9",
        ["alert"] = "M10 7v4M10 13.8v.1M8.7 3.3L2.6 14.2A1.5 1.5 0 003.9 16.5h12.2a1.5 1.5 0 001.3-2.3L11.3 3.3a1.5 1.5 0 00-2.6 0z",
        ["bell"] = "M5.5 13.5V9a4.5 4.5 0 019 0v4.5l1.5 2h-12zM8.3 17.5a1.9 1.9 0 003.4 0",
        ["clock"] = "M10 17.5a7.5 7.5 0 100-15 7.5 7.5 0 000 15zM10 6v4.2l2.8 1.8",
        ["cal"] = "M4 5.5h12v11H4zM4 8.5h12M7.5 3.5v3M12.5 3.5v3",
        ["gauge"] = "M3.5 14a6.5 6.5 0 1113 0M10 14l3-4.5",
        ["home"] = "M3.5 9L10 3.5 16.5 9v7.5H12v-5H8v5H3.5z",
        ["user"] = "M10 10a3.5 3.5 0 100-7 3.5 3.5 0 000 7zM3.5 17a6.5 6.5 0 0113 0",
        ["users"] = "M7.5 9.5a3 3 0 100-6 3 3 0 000 6zM2 16.5a5.5 5.5 0 0111 0M13 3.7a3 3 0 010 5.6M15 11.3a5.5 5.5 0 013 5.2",
        ["file"] = "M5.5 2.5h6L15 6v11.5H5.5zM11.5 2.5V6H15M8 10h4.5M8 13h4.5",
        ["pill"] = "M7.2 16.3a3.4 3.4 0 01-4.8-4.8l6-6a3.4 3.4 0 014.8 4.8zM5.4 8.4l4.8 4.8",
        ["search"] = "M9 15a6 6 0 100-12 6 6 0 000 12zM13.5 13.5L17 17",
        ["logout"] = "M8 3.5H4.5v13H8M12 6.5l3.5 3.5-3.5 3.5M15.5 10h-8",
        ["settings"] = "M10 12.5a2.5 2.5 0 100-5 2.5 2.5 0 000 5zM10 2.5v2M10 15.5v2M2.5 10h2M15.5 10h2M4.7 4.7l1.4 1.4M13.9 13.9l1.4 1.4M4.7 15.3l1.4-1.4M13.9 6.1l1.4-1.4",
        ["chart"] = "M3.5 16.5h13M6 13.5v-4M10 13.5v-8M14 13.5v-6",
        ["list"] = "M7 5.5h9.5M7 10h9.5M7 14.5h9.5M3.5 5.5v.1M3.5 10v.1M3.5 14.5v.1",
        ["plus"] = "M10 4.5v11M4.5 10h11",
        ["queue"] = "M4 4.5h12M4 10h12M4 15.5h7",
        ["key"] = "M7 13a4 4 0 110-8 4 4 0 010 8zM10.5 9h7M15 9v3M17.5 9v2",
        ["menu"] = "M3.5 5.5h13M3.5 10h13M3.5 14.5h13",
        ["more"] = "M5 10h.01M10 10h.01M15 10h.01",
        ["info"] = "M10 9v5M10 6.2v.1M10 17.5a7.5 7.5 0 100-15 7.5 7.5 0 000 15z",
        ["chevron"] = "M7.5 5l5 5-5 5",
        ["sun"] ="M10 13.5a3.5 3.5 0 100-7 3.5 3.5 0 000 7zM10 2.5v1.5M10 16v1.5M2.5 10H4M16 10h1.5M4.7 4.7l1.1 1.1M14.2 14.2l1.1 1.1M4.7 15.3l1.1-1.1M14.2 5.8l1.1-1.1",
        ["moon"] = "M16 12.2A6.5 6.5 0 017.8 4a6.5 6.5 0 108.2 8.2z",
        ["auto"] = "M3 4.5h14v9H3zM7.5 16.5h5M10 13.5v3",
        ["walk"] ="M11 4.2v.1M9.5 7.5l-2 3.5h3l-1.5 6M10.5 11l2.5 2.5M9.5 7.5l3 1.5 1.5 2",
    };

    public static IHtmlContent Icon(string name, bool small = false) =>
        new HtmlString(
            $"<svg class=\"mc-ico{(small ? " mc-ico-sm" : string.Empty)}\" viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><path d=\"{Paths[name]}\"/></svg>");

    /// <summary>The heart-and-cross mark (Concept 2). Colours come from the stylesheet (.mc-mark-heart / .mc-mark-swoosh / .mc-mark-cross).</summary>
    public static IHtmlContent Mark(int size = 36) =>
        new HtmlString(
            $"<svg class=\"mc-mark\" viewBox=\"0 0 64 64\" width=\"{size}\" height=\"{size}\" aria-hidden=\"true\" focusable=\"false\">" +
            "<path class=\"mc-mark-heart\" d=\"M32 56C15 44 5.5 34 5.5 22A13 13 0 0 1 32 15.5 13 13 0 0 1 58.5 22C58.5 34 49 44 32 56z\" fill=\"none\" stroke-width=\"5\" stroke-linejoin=\"round\"/>" +
            "<path class=\"mc-mark-swoosh\" d=\"M45.25 5.1A13.64 13.64 0 0 1 58.5 22\" fill=\"none\" stroke-width=\"5\" stroke-linecap=\"round\"/>" +
            "<path class=\"mc-mark-cross\" d=\"M28 23.5h8v7h7v8h-7v7h-8v-7h-7v-8h7z\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/></svg>");
}
