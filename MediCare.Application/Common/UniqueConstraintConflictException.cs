namespace MediCare.Application.Common;

/// <summary>
/// A database unique index rejected a change after the retry wrapper gave up. The message is already user-facing, so
/// callers can show it as a normal refusal instead of an error page. Derives from InvalidOperationException so existing
/// refusal handling applies.
/// </summary>
public sealed class UniqueConstraintConflictException(string constraintName, string friendlyMessage, Exception? innerException = null)
    : InvalidOperationException(friendlyMessage, innerException)
{
    /// <summary>The SQL Server index name that rejected the change (for logs; never shown to users).</summary>
    public string ConstraintName { get; } = constraintName;
}
