using MediCare.Web.Middleware;

namespace MediCare.Tests;

public sealed class SessionStampPolicyTests
{
    [Theory]
    [InlineData("A", "A", true)]
    [InlineData("A", "B", false)]
    [InlineData(null, "B", true)]
    [InlineData("A", null, true)]
    public void Session_is_valid_only_while_stamps_match(string? cookieStamp, string? accountStamp, bool expected) =>
        Assert.Equal(expected, SessionStampPolicy.Matches(cookieStamp, accountStamp));
}
