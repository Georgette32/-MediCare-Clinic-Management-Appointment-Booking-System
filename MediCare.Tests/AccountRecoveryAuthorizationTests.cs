using System.Reflection;
using MediCare.Infrastructure;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using MediCare.Web.Controllers;
using MediCare.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Tests;

public sealed class AccountRecoveryAuthorizationTests
{
    [Fact]
    public void Staff_password_reset_is_admin_only_at_class_level()
    {
        var authorization = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Admin, authorization!.Roles);
    }

    [Fact]
    public void Staff_reset_confirm_is_a_get_and_the_reset_is_an_antiforgery_protected_post()
    {
        var confirm = typeof(AdminController).GetMethod(nameof(AdminController.ConfirmResetStaffPassword));
        Assert.NotNull(confirm);
        Assert.NotNull(confirm!.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(confirm.GetCustomAttribute<AuthorizeAttribute>());

        var reset = typeof(AdminController).GetMethod(nameof(AdminController.ResetStaffPassword));
        Assert.NotNull(reset);
        Assert.NotNull(reset!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(reset.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(reset.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Patient_password_reset_is_reception_and_admin_only()
    {
        var authorization = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        var roles = authorization!.Roles!.Split(',');
        Assert.Contains(MediCareRoles.Receptionist, roles);
        Assert.Contains(MediCareRoles.Admin, roles);
        Assert.DoesNotContain(MediCareRoles.Doctor, roles);
        Assert.DoesNotContain(MediCareRoles.Patient, roles);

        var confirm = typeof(ReceptionController).GetMethod(nameof(ReceptionController.ConfirmResetPatientPassword));
        Assert.NotNull(confirm);
        Assert.NotNull(confirm!.GetCustomAttribute<HttpGetAttribute>());

        var reset = typeof(ReceptionController).GetMethod(nameof(ReceptionController.ResetPatientPassword));
        Assert.NotNull(reset);
        Assert.NotNull(reset!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(reset.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(reset.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Theory]
    [InlineData(nameof(AccountController.Login), typeof(MediCare.Web.Models.LoginViewModel), RateLimitPolicies.Auth)]
    [InlineData(nameof(AccountController.ChangePassword), typeof(MediCare.Web.Models.ChangePasswordViewModel), RateLimitPolicies.Auth)]
    [InlineData(nameof(AccountController.Register), typeof(MediCare.Web.Models.PatientRegistrationViewModel), RateLimitPolicies.Register)]
    public void Sign_in_password_change_and_registration_posts_are_rate_limited(string actionName, Type modelType, string policy)
    {
        var action = typeof(AccountController).GetMethods()
            .Single(method => method.Name == actionName && method.GetParameters().Length >= 1 && method.GetParameters()[0].ParameterType == modelType);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal(policy, action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void Password_policy_matches_the_agreed_values_from_the_real_identity_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MediCareDbContext>(options =>
            options.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=UnusedByOptionsTest;Trusted_Connection=True"));
        services.AddMediCareIdentity();
        using var provider = services.BuildServiceProvider();

        var identity = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>().Value;
        Assert.Equal(12, identity.Password.RequiredLength);
        Assert.True(identity.Password.RequireDigit);
        Assert.True(identity.Password.RequireLowercase);
        Assert.True(identity.Password.RequireUppercase);
        Assert.True(identity.Password.RequireNonAlphanumeric);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identity.Lockout.DefaultLockoutTimeSpan);
        Assert.True(identity.Lockout.AllowedForNewUsers);
    }

}
