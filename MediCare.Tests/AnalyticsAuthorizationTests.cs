using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

/// <summary>Authorization matrix for queue analytics: Doctor own stats, Receptionist operational, Admin full clinic.</summary>
public sealed class AnalyticsAuthorizationTests
{
    private static HashSet<string> RolesOf(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>().Select(item => item.Roles ?? string.Empty)
            .SelectMany(roles => roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet();

    [Fact]
    public void Analytics_controller_is_limited_to_admin_and_receptionist()
    {
        var roles = RolesOf(typeof(AnalyticsController));

        Assert.Contains(MediCareRoles.Admin, roles);
        Assert.Contains(MediCareRoles.Receptionist, roles);
        Assert.DoesNotContain(MediCareRoles.Doctor, roles);
        Assert.DoesNotContain(MediCareRoles.Patient, roles);
    }

    [Theory]
    [InlineData(nameof(AnalyticsController.Index))]
    [InlineData(nameof(AnalyticsController.Doctor))]
    public void Full_clinic_and_doctor_drilldown_are_admin_only(string actionName)
    {
        var action = typeof(AnalyticsController).GetMethod(actionName)!;

        Assert.Equal(MediCareRoles.Admin, action.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Live_board_is_available_to_receptionist_and_admin_only()
    {
        var action = typeof(AnalyticsController).GetMethod(nameof(AnalyticsController.Live))!;
        var roles = action.GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');

        Assert.Contains(MediCareRoles.Receptionist, roles);
        Assert.Contains(MediCareRoles.Admin, roles);
        Assert.DoesNotContain(MediCareRoles.Doctor, roles);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Doctor_statistics_is_doctor_only_and_resolves_the_doctor_from_the_user()
    {
        Assert.Equal(MediCareRoles.Doctor, typeof(DoctorController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);

        var action = typeof(DoctorController).GetMethod(nameof(DoctorController.Statistics))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        // No doctor id parameter: ownership comes from the signed-in user, never from the request.
        Assert.DoesNotContain(action.GetParameters(), parameter => parameter.Name is "id" or "doctorId");
    }

    [Fact]
    public void Analytics_pages_do_not_accept_post_writes()
    {
        var posts = typeof(AnalyticsController).GetMethods()
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null);

        Assert.Empty(posts);
    }
}
