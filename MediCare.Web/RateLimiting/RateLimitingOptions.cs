using Microsoft.Extensions.Configuration;

namespace MediCare.Web.RateLimiting;

/// <summary>
/// Fixed-window limits per client IP, read from the <c>RateLimiting</c> configuration section. There are no defaults in code:
/// a missing or non-positive value stops the application at startup.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitRule Auth { get; set; } = new();
    public RateLimitRule Register { get; set; } = new();

    /// <summary>Binds and validates the section. Throws <see cref="InvalidOperationException"/> listing every invalid value.</summary>
    public static RateLimitingOptions Load(IConfiguration configuration)
    {
        var options = new RateLimitingOptions();
        configuration.GetSection(SectionName).Bind(options);

        var errors = Validate(options);
        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid RateLimiting configuration: " + string.Join(" ", errors));
        return options;
    }

    public static IReadOnlyList<string> Validate(RateLimitingOptions options)
    {
        var errors = new List<string>();
        ValidateRule(nameof(Auth), options.Auth, errors);
        ValidateRule(nameof(Register), options.Register, errors);
        return errors;
    }

    private static void ValidateRule(string name, RateLimitRule? rule, List<string> errors)
    {
        if (rule is null)
        {
            errors.Add($"{SectionName}:{name} is required.");
            return;
        }
        if (rule.PermitLimit <= 0)
            errors.Add($"{SectionName}:{name}:PermitLimit must be greater than zero.");
        if (rule.WindowSeconds <= 0)
            errors.Add($"{SectionName}:{name}:WindowSeconds must be greater than zero.");
    }
}

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}
