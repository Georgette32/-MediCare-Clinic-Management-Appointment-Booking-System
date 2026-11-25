using System.Globalization;
using System.Net;
using MediCare.Application.Services;
using MediCare.Domain.Enums;
using Microsoft.AspNetCore.Html;

namespace MediCare.Web.Models;

/// <summary>
/// Display helpers for the queue screens: status badges that never rely on colour alone (each tone has its own glyph
/// shape and a word), ticket numbers, and the grace-period ring. Labels come from <see cref="QueueBoardText"/>.
/// </summary>
public static class QueueUi
{
    private enum Glyph { Ring, Half, Dot, Tick, Cross, Dash }

    public static IHtmlContent Badge(ScheduledBoardState state) => state switch
    {
        ScheduledBoardState.NotArrived => Render("slate", Glyph.Ring, QueueBoardText.Scheduled(state)),
        ScheduledBoardState.GraceRunning => Render("amber", Glyph.Half, QueueBoardText.Scheduled(state)),
        ScheduledBoardState.GraceExpired => Render("vermilion", Glyph.Cross, "Grace expired"),
        ScheduledBoardState.Waiting => Render("slate", Glyph.Ring, QueueBoardText.Scheduled(state)),
        ScheduledBoardState.Called => Render("amber", Glyph.Dot, QueueBoardText.Scheduled(state)),
        ScheduledBoardState.InConsultation => Render("teal", Glyph.Half, QueueBoardText.Scheduled(state)),
        ScheduledBoardState.Completed => Render("cobalt", Glyph.Tick, QueueBoardText.Scheduled(state)),
        ScheduledBoardState.NoShow => Render("vermilion", Glyph.Cross, QueueBoardText.Scheduled(state)),
        _ => Render("slate", Glyph.Ring, QueueBoardText.Scheduled(state))
    };

    public static IHtmlContent Badge(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Waiting => Render("slate", Glyph.Ring, QueueBoardText.Walk(status)),
        QueueEntryStatus.Called => Render("amber", Glyph.Dot, QueueBoardText.Walk(status)),
        QueueEntryStatus.InConsultation => Render("teal", Glyph.Half, QueueBoardText.Walk(status)),
        QueueEntryStatus.Completed => Render("cobalt", Glyph.Tick, QueueBoardText.Walk(status)),
        QueueEntryStatus.NoShow => Render("vermilion", Glyph.Cross, QueueBoardText.Walk(status)),
        _ => Render("slate", Glyph.Dash, QueueBoardText.Walk(status))
    };

    /// <summary>The walk-in's admission approval, worded for Reception.</summary>
    public static IHtmlContent Badge(WalkInApprovalState state) => state switch
    {
        WalkInApprovalState.Pending => Render("amber", Glyph.Half, "Waiting for the doctor"),
        WalkInApprovalState.Approved => Render("teal", Glyph.Tick, "Doctor approved"),
        WalkInApprovalState.Rejected => Render("vermilion", Glyph.Cross, "Doctor said not yet"),
        WalkInApprovalState.Expired => Render("slate", Glyph.Dash, "Approval request expired"),
        WalkInApprovalState.Used => Render("slate", Glyph.Dash, "Approval used"),
        _ => Render("slate", Glyph.Ring, "Needs the doctor's approval")
    };

    /// <summary>An appointment's status in the product's words, with a glyph so it never depends on colour alone.</summary>
    public static IHtmlContent Badge(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => Render("amber", Glyph.Half, "Waiting for confirmation"),
        AppointmentStatus.Confirmed => Render("teal", Glyph.Tick, "Confirmed"),
        AppointmentStatus.Completed => Render("cobalt", Glyph.Tick, "Completed"),
        AppointmentStatus.NoShow => Render("vermilion", Glyph.Cross, "No-show"),
        AppointmentStatus.Rejected => Render("vermilion", Glyph.Cross, "Rejected"),
        AppointmentStatus.Cancelled => Render("slate", Glyph.Dash, "Cancelled"),
        AppointmentStatus.Rescheduled => Render("slate", Glyph.Dash, "Moved to a new time"),
        _ => Render("slate", Glyph.Ring, status.ToString())
    };

    /// <summary>Short label for filter chips.</summary>
    public static string Label(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => "Waiting for confirmation",
        AppointmentStatus.NoShow => "No-show",
        AppointmentStatus.Rescheduled => "Moved",
        _ => status.ToString()
    };

    /// <summary>Walk-ins keep their real queue number (W-07). Scheduled patients are known by their appointment time.</summary>
    public static string WalkInNumber(long sequence) => "W-" + sequence.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Minutes of grace left and the grace length, anchored to the appointment start (grace = 3 x expected duration,
    /// frozen per appointment). Returns null when the item has no grace end.
    /// </summary>
    public static (int Total, int Left)? Grace(BoardScheduledItem item, DateOnly date, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (item.GraceEndsAtUtc is not DateTimeOffset end)
            return null;
        var local = date.ToDateTime(item.StartTime);
        var start = new DateTimeOffset(local, zone.GetUtcOffset(local));
        var total = Math.Max(1, (int)Math.Round((end - start).TotalMinutes));
        var left = Math.Clamp((int)Math.Ceiling((end - now).TotalMinutes), 0, total);
        return (total, left);
    }

    /// <summary>The grace ring: a server-drawn SVG countdown. Teal while time remains, amber in the last quarter, red when out.</summary>
    public static IHtmlContent GraceRing(int total, int left, int size = 88)
    {
        var stroke = size / 10.0;
        var radius = (size - stroke) / 2;
        var circumference = 2 * Math.PI * radius;
        var fraction = left <= 0 ? 1 : left / (double)total;
        var state = left <= 0 ? "out" : fraction <= 0.25 ? "late" : "ok";
        string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        var half = F(size / 2.0);
        return new HtmlString(
            $"<div class=\"mc-grace mc-grace-{state}\"><div class=\"mc-grace-dial\" style=\"width:{size}px;height:{size}px\">" +
            $"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\" aria-hidden=\"true\">" +
            $"<circle class=\"mc-grace-track\" cx=\"{half}\" cy=\"{half}\" r=\"{F(radius)}\" stroke-width=\"{F(stroke)}\"/>" +
            $"<circle class=\"mc-grace-arc\" cx=\"{half}\" cy=\"{half}\" r=\"{F(radius)}\" stroke-width=\"{F(stroke)}\" stroke-dasharray=\"{F(circumference)}\" stroke-dashoffset=\"{F(circumference * (1 - fraction))}\"/></svg>" +
            $"<div class=\"mc-grace-mid\"><b style=\"font-size:{F(size * 0.32)}px\">{left}</b><span>min</span></div></div></div>");
    }

    private static IHtmlContent Render(string tone, Glyph glyph, string label)
    {
        var shape = glyph switch
        {
            Glyph.Ring => "<circle cx=\"6\" cy=\"6\" r=\"4.25\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"/>",
            Glyph.Dot => "<circle cx=\"6\" cy=\"6\" r=\"5\" fill=\"currentColor\"/>",
            Glyph.Half => "<circle cx=\"6\" cy=\"6\" r=\"4.25\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"/><path d=\"M6 1.75a4.25 4.25 0 010 8.5z\" fill=\"currentColor\"/>",
            Glyph.Tick => "<path d=\"M2 6.5l2.5 2.5L10 3\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
            Glyph.Cross => "<path d=\"M2.5 2.5l7 7M9.5 2.5l-7 7\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>",
            _ => "<path d=\"M2.5 6h7\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/>"
        };
        return new HtmlString($"<span class=\"mc-badge mc-tone-{tone}\"><svg viewBox=\"0 0 12 12\" aria-hidden=\"true\">{shape}</svg>{WebUtility.HtmlEncode(label)}</span>");
    }
}
