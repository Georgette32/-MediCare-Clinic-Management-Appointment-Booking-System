using System.Text.RegularExpressions;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

/// <summary>
/// Plain-words checks for what people type, so a mistake gets a message that says exactly what is wrong.
/// The same rules run in the browser (site.js, data-check attributes); these are the server's copy.
/// </summary>
public static partial class InputChecks
{
    /// <summary>True when the text is made only of digits and phone punctuation (+, spaces, dashes, brackets).</summary>
    public static bool LooksLikePhone(string text) =>
        text.Length > 0 && text.Any(char.IsDigit) && text.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')');

    /// <summary>
    /// What is wrong with a full Egyptian mobile number, or null when it is valid. Assumes <see cref="LooksLikePhone"/>.
    /// </summary>
    public static string? FullMobileProblem(string text)
    {
        if (EgyptianMobileNumber.TryNormalize(text, out _, out _))
            return null;

        var trimmed = text.Trim();
        if (trimmed.IndexOf('+') > 0)
            return "Put + only at the start of the number, as in +201012345678.";

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        string national;
        if (trimmed.StartsWith('+'))
        {
            if (!digits.StartsWith("20", StringComparison.Ordinal))
                return "Only Egyptian numbers are accepted: start with +20 or 01.";
            national = digits[2..];
        }
        else if (digits.StartsWith("0020", StringComparison.Ordinal))
            national = digits[4..];
        else if (digits.StartsWith("0", StringComparison.Ordinal))
        {
            if (!digits.StartsWith("01", StringComparison.Ordinal))
                return "Egyptian mobile numbers start with 01.";
            national = digits[1..];
        }
        else if (digits.StartsWith("1", StringComparison.Ordinal))
            return "Add the leading 0: Egyptian mobile numbers start with 01.";
        else
            return "Egyptian mobile numbers start with 01.";

        // national is the 10 digits after the country code / leading zero: 1X XXXX XXXX
        if (national.Length < 10)
            return $"This number is {10 - national.Length} digit{(10 - national.Length == 1 ? "" : "s")} short: a mobile number has 11 digits, like 01012345678.";
        if (national.Length > 10)
            return $"This number has {national.Length - 10} digit{(national.Length - 10 == 1 ? "" : "s")} too many: a mobile number has 11 digits, like 01012345678.";
        if (national[0] != '1' || national[1] is not ('0' or '1' or '2' or '5'))
            return "Egyptian mobile numbers start with 010, 011, 012 or 015.";
        return "Enter a valid Egyptian mobile number, like 01012345678.";
    }

    /// <summary>
    /// What is wrong with a patient search (phone or name), or null when it can run. Part of a number (3 digits or more)
    /// is a valid search; a number that is clearly meant to be complete must be a valid mobile number.
    /// </summary>
    public static string? PatientSearchProblem(string? query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
            return null;

        if (LooksLikePhone(text))
        {
            var digits = text.Count(char.IsDigit);
            if (digits < 3)
                return "Type at least 3 digits to search by phone number.";
            var meantComplete = text.StartsWith('+') || text.StartsWith("00", StringComparison.Ordinal) || digits >= 10;
            return meantComplete ? FullMobileProblem(text) : null;
        }

        if (text.Any(char.IsDigit))
            return "Search by a phone number or by a name, not both: remove the letters or the digits.";
        if (text.Count(char.IsLetter) < 2)
            return "Type at least 2 letters of the patient’s name.";
        if (!NameCharacters().IsMatch(text))
            return "Names can only contain letters, spaces, dots, apostrophes and dashes.";
        return null;
    }

    /// <summary>What is wrong with the sign-in username field, or null when it is worth trying.</summary>
    public static string? LoginUserNameProblem(string? userName)
    {
        var text = (userName ?? string.Empty).Trim();
        if (text.Length == 0)
            return "Enter your mobile number (patients) or your staff username.";
        if (LooksLikePhone(text))
            return FullMobileProblem(text);
        if (text.Any(char.IsWhiteSpace))
            return "Usernames don’t contain spaces. Check for a space before or inside it.";
        if (text.Contains('@'))
        {
            var at = text.IndexOf('@');
            if (at == 0)
                return "Add the part before @, as in name@clinic.com.";
            if (text.Count(c => c == '@') > 1)
                return "An email address has only one @.";
            var domain = text[(at + 1)..];
            if (domain.Length == 0)
                return "Add the part after @, as in name@clinic.com.";
            if (!domain.Contains('.') || domain.EndsWith('.') || domain.StartsWith('.'))
                return "The part after @ looks incomplete, as in name@clinic.com.";
        }
        else if (text.Any(char.IsDigit) && text.Count(char.IsDigit) >= 8 && text.Any(char.IsLetter))
            return "This mixes letters into a phone number. Patients sign in with their mobile number only, like 01012345678.";
        return null;
    }

    /// <summary>What is wrong with the sign-in password field, or null.</summary>
    public static string? LoginPasswordProblem(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Enter your password.";
        if (password.Trim().Length == 0)
            return "Your password can’t be only spaces.";
        if (password.Length > 128)
            return "Passwords are at most 128 characters. Check that you pasted the right text.";
        return null;
    }

    [GeneratedRegex(@"^[\p{L}\p{M}\s.'’\-]+$")]
    private static partial Regex NameCharacters();
}
