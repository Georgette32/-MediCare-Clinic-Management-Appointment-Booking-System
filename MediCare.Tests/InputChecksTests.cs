using MediCare.Web.Controllers;
using MediCare.Web.Models;

namespace MediCare.Tests;

public sealed class InputChecksTests
{
    [Theory]
    [InlineData("01012345678")]
    [InlineData("+20 101 234 5678")]
    [InlineData("0020-1012345678")]
    [InlineData("01512345678")]
    public void Valid_mobile_numbers_have_no_problem(string input) =>
        Assert.Null(InputChecks.FullMobileProblem(input));

    [Theory]
    [InlineData("0101234567", "1 digit short")]
    [InlineData("010123456", "2 digits short")]
    [InlineData("010123456789", "1 digit too many")]
    [InlineData("01312345678", "010, 011, 012 or 015")]
    [InlineData("02123456789", "start with 01")]
    [InlineData("1012345678", "Add the leading 0")]
    [InlineData("+44 7700 900123", "Only Egyptian numbers")]
    [InlineData("010+12345678", "+ only at the start")]
    [InlineData("+20101234567", "1 digit short")]
    public void Wrong_mobile_numbers_say_exactly_what_is_wrong(string input, string expected) =>
        Assert.Contains(expected, InputChecks.FullMobileProblem(input));

    [Theory]
    [InlineData("")]
    [InlineData("010")]          // part of a number: "contains" search
    [InlineData("0101234")]
    [InlineData("01012345678")]
    [InlineData("Sara")]
    [InlineData("Abd El-Rahman")]
    [InlineData("سارة")]
    public void Patient_searches_that_can_run_have_no_problem(string query) =>
        Assert.Null(InputChecks.PatientSearchProblem(query));

    [Theory]
    [InlineData("01", "at least 3 digits")]
    [InlineData("0101234567", "1 digit short")]
    [InlineData("010123456789", "too many")]
    [InlineData("01312345678", "010, 011, 012 or 015")]
    [InlineData("+2010123", "short")]
    [InlineData("Sara 010", "not both")]
    [InlineData("S", "at least 2 letters")]
    [InlineData("Sara!", "letters, spaces")]
    public void Patient_searches_that_cannot_be_right_explain_why(string query, string expected) =>
        Assert.Contains(expected, InputChecks.PatientSearchProblem(query));

    [Theory]
    [InlineData("01012345678")]
    [InlineData("reception.demo@medicare.test")]
    [InlineData("admin")]
    public void Plausible_usernames_have_no_problem(string input) =>
        Assert.Null(InputChecks.LoginUserNameProblem(input));

    [Theory]
    [InlineData("", "Enter your mobile number")]
    [InlineData("0101234567", "1 digit short")]
    [InlineData("doctor @clinic.test", "don’t contain spaces")]
    [InlineData("@clinic.test", "before @")]
    [InlineData("doctor@", "after @")]
    [InlineData("doctor@clinic", "looks incomplete")]
    [InlineData("a@b@c.com", "only one @")]
    [InlineData("0101234567o8", "mixes letters")]
    public void Wrong_usernames_say_exactly_what_is_wrong(string input, string expected) =>
        Assert.Contains(expected, InputChecks.LoginUserNameProblem(input));

    [Theory]
    [InlineData("", "Enter your password")]
    [InlineData("   ", "only spaces")]
    public void Wrong_passwords_say_what_is_wrong(string input, string expected) =>
        Assert.Contains(expected, InputChecks.LoginPasswordProblem(input));

    [Fact]
    public void Failed_sign_in_message_fits_what_was_typed_without_revealing_the_account()
    {
        Assert.StartsWith("The mobile number or password is incorrect.", AccountController.LoginFailureMessage("01012345678"));
        Assert.StartsWith("The username or password is incorrect.", AccountController.LoginFailureMessage("doctor@clinic.test"));
    }
}
