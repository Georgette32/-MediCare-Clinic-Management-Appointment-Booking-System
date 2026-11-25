namespace MediCare.Web.Models;

/// <summary>Role-specific shortcuts shown in the account popover of the top bar (one per role the user holds).</summary>
public static class UserMenu
{
    public static IReadOnlyList<(string Label, string Hint, string Controller, string Action, string Icon)> ShortcutsFor(
        bool isPatient, bool isDoctor, bool isReception, bool isAdmin)
    {
        var items = new List<(string, string, string, string, string)>();
        if (isPatient)
            items.Add(("My appointments", "Upcoming and past visits", "Appointments", "Mine", "cal"));
        if (isDoctor)
            items.Add(("My schedule", "Working hours, breaks and days off", "Doctor", "Schedule", "cal"));
        if (isReception && !isAdmin)
            items.Add(("Today’s queue", "Check in and call patients", "Reception", "Queue", "queue"));
        if (isAdmin)
            items.Add(("Clinic settings", "Clinic-wide rules", "Admin", "Settings", "settings"));
        return items;
    }
}
