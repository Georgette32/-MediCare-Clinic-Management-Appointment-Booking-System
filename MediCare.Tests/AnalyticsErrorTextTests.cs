using MediCare.Web.Models;

namespace MediCare.Tests;

public sealed class AnalyticsErrorTextTests
{
    [Fact]
    public void Removes_the_framework_parameter_suffix()
    {
        var exception = new ArgumentException("Historical analytics must end before today.", "toDate");
        Assert.Equal("Historical analytics must end before today.", AnalyticsErrorText.From(exception));
    }

    [Fact]
    public void Keeps_messages_without_a_parameter()
    {
        Assert.Equal("Bad range.", AnalyticsErrorText.From(new ArgumentException("Bad range.")));
    }
}
