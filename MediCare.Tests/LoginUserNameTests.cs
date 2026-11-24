using MediCare.Web.Models;

namespace MediCare.Tests;

public sealed class LoginUserNameTests
{
    [Theory]
    [InlineData("01012345678", "+201012345678")]
    [InlineData("+20 101 234 5678", "+201012345678")]
    [InlineData("0020-1012345678", "+201012345678")]
    public void Egyptian_phone_formats_resolve_to_canonical_username(string input, string expected) =>
        Assert.Equal(expected, LoginUserName.Resolve(input));

    [Theory]
    [InlineData("doctor@clinic.test", "doctor@clinic.test")]
    [InlineData(" admin@x.test ", "admin@x.test")]
    public void Email_usernames_are_only_trimmed(string input, string expected) =>
        Assert.Equal(expected, LoginUserName.Resolve(input));
}
