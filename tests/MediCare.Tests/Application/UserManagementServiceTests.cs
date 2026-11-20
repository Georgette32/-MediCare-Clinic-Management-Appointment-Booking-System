using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Users;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Services;
using MediCare.Application.Validation;
using Moq;

namespace MediCare.Tests.Application;

public class UserManagementServiceTests
{
    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<IDoctorRepository> _doctors = new();

    private UserManagementService CreateService() =>
        new(_identity.Object, _doctors.Object, new ChangeUserRoleRequestValidator());

    private static IdentityUserInfo User(Guid id, string email = "user@example.com") =>
        new(id, email, "Test User", null, true, true, DateTime.UtcNow);

    [Fact]
    public async Task ChangeRoleAsync_RequiresAdministrator()
    {
        var result = await CreateService().ChangeRoleAsync(Guid.Empty, new ChangeUserRoleRequest
        {
            UserId = Guid.NewGuid(),
            Role = Roles.Patient
        });

        Assert.False(result.Succeeded);
        Assert.Contains("administrator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeRoleAsync_CannotChangeOwnRole()
    {
        var adminId = Guid.NewGuid();

        var result = await CreateService().ChangeRoleAsync(adminId, new ChangeUserRoleRequest
        {
            UserId = adminId,
            Role = Roles.Patient
        });

        Assert.False(result.Succeeded);
        Assert.Contains("your own role", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeRoleAsync_CannotDemoteLastAdministrator()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _identity
            .Setup(x => x.FindByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(targetId, "admin@example.com"));
        _identity
            .Setup(x => x.GetRolesAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Roles.Admin });
        _identity
            .Setup(x => x.CountUsersInRoleAsync(Roles.Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateService().ChangeRoleAsync(adminId, new ChangeUserRoleRequest
        {
            UserId = targetId,
            Role = Roles.Patient
        });

        Assert.False(result.Succeeded);
        Assert.Contains("last administrator", result.Error, StringComparison.OrdinalIgnoreCase);
        _identity.Verify(x => x.RemoveFromRoleAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeRoleAsync_CannotAssignDoctorRoleWithoutProfile()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _identity
            .Setup(x => x.FindByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(targetId));
        _identity
            .Setup(x => x.GetRolesAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Roles.Patient });
        _doctors
            .Setup(x => x.ExistsForUserAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService().ChangeRoleAsync(adminId, new ChangeUserRoleRequest
        {
            UserId = targetId,
            Role = Roles.Doctor
        });

        Assert.False(result.Succeeded);
        Assert.Contains("doctor registration", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeRoleAsync_ReplacesRolesAndRefreshesSecurityStamp()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _identity
            .Setup(x => x.FindByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(targetId));
        _identity
            .Setup(x => x.GetRolesAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Roles.Patient });
        _doctors
            .Setup(x => x.ExistsForUserAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _identity
            .Setup(x => x.RemoveFromRoleAsync(targetId, Roles.Patient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        _identity
            .Setup(x => x.AddToRoleAsync(targetId, Roles.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        _identity
            .Setup(x => x.RefreshSecurityStampAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());

        var result = await CreateService().ChangeRoleAsync(adminId, new ChangeUserRoleRequest
        {
            UserId = targetId,
            Role = Roles.Doctor
        });

        Assert.True(result.Succeeded);
        _identity.Verify(x => x.RemoveFromRoleAsync(targetId, Roles.Patient, It.IsAny<CancellationToken>()), Times.Once);
        _identity.Verify(x => x.AddToRoleAsync(targetId, Roles.Doctor, It.IsAny<CancellationToken>()), Times.Once);
        _identity.Verify(x => x.RefreshSecurityStampAsync(targetId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenRoleIsUnchanged_IsNoOp()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _identity
            .Setup(x => x.FindByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(targetId));
        _identity
            .Setup(x => x.GetRolesAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Roles.Patient });

        var result = await CreateService().ChangeRoleAsync(adminId, new ChangeUserRoleRequest
        {
            UserId = targetId,
            Role = Roles.Patient
        });

        Assert.True(result.Succeeded);
        _identity.Verify(x => x.RemoveFromRoleAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _identity.Verify(x => x.AddToRoleAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetActiveAsync_CannotDeactivateSelf()
    {
        var adminId = Guid.NewGuid();

        var result = await CreateService().SetActiveAsync(adminId, adminId, false);

        Assert.False(result.Succeeded);
        Assert.Contains("your own account", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActiveAsync_CannotDeactivateLastAdministrator()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _identity
            .Setup(x => x.FindByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(targetId, "admin@example.com"));
        _identity
            .Setup(x => x.GetRolesAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Roles.Admin });
        _identity
            .Setup(x => x.CountUsersInRoleAsync(Roles.Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateService().SetActiveAsync(adminId, targetId, false);

        Assert.False(result.Succeeded);
        Assert.Contains("last administrator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActiveAsync_DelegatesToIdentity()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _identity
            .Setup(x => x.FindByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User(targetId));
        _identity
            .Setup(x => x.GetRolesAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Roles.Patient });
        _identity
            .Setup(x => x.SetActiveAsync(targetId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());

        var result = await CreateService().SetActiveAsync(adminId, targetId, false);

        Assert.True(result.Succeeded);
        _identity.Verify(x => x.SetActiveAsync(targetId, false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
