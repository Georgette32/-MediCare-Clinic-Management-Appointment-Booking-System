namespace MediCare.Web.Models;

/// <summary>User-facing text for analytics argument errors, without the framework's "(Parameter 'x')" suffix.</summary>
public static class AnalyticsErrorText
{
    public static string From(ArgumentException exception)
    {
        var message = exception.Message;
        if (exception.ParamName is { Length: > 0 } name)
        {
            var suffix = $" (Parameter '{name}')";
            if (message.EndsWith(suffix, StringComparison.Ordinal))
                message = message[..^suffix.Length];
        }
        return message;
    }
}
