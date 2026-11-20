
using MediCare.Application.Common;
using MediCare.Application.DTOs.Analytics;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class DoctorRepository : IDoctorRepository
{
    private readonly ApplicationDbContext _dbContext;

    public DoctorRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DoctorProfile?> GetByIdAsync(
        Guid id,
        bool track = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DoctorProfiles
            .Include(d => d.Specialization)
            .AsQueryable();

        if (!track)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(
            d => d.Id == id, cancellationToken);
    }

    public async Task<DoctorProfile?> GetByUserIdAsync(
        Guid userId,
        bool track = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DoctorProfiles
            .Include(d => d.Specialization)
            .AsQueryable();

        if (!track)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(
            d => d.UserId == userId, cancellationToken);
    }

    public Task<bool> ExistsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles.AnyAsync(
            d => d.UserId == userId, cancellationToken);

    public Task<bool> LicenseNumberExistsAsync(
        string licenseNumber,
        Guid? excludeDoctorId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = licenseNumber.Trim();

        return _dbContext.DoctorProfiles.AnyAsync(
            d => d.LicenseNumber == trimmed &&
                 (!excludeDoctorId.HasValue || d.Id != excludeDoctorId.Value),
            cancellationToken);
    }

    public async Task<PagedResult<DoctorProfile>> GetPagedAsync(
        DoctorQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<DoctorProfile> doctors = _dbContext.DoctorProfiles
            .Include(d => d.Specialization)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            doctors = doctors.Where(d =>
                d.LicenseNumber.Contains(term) ||
                d.Specialization.Name.Contains(term) ||
                _dbContext.Users.Any(u =>
                    u.Id == d.UserId &&
                    (u.FullName.Contains(term) ||
                     (u.Email != null && u.Email.Contains(term)))));
        }

        if (query.ApprovalStatus.HasValue)
            doctors = doctors.Where(
                d => d.ApprovalStatus == query.ApprovalStatus.Value);

        if (query.IsActive.HasValue)
        {
            var isActive = query.IsActive.Value;

            doctors = doctors.Where(
                d => _dbContext.Users.Any(
                    u => u.Id == d.UserId && u.IsActive == isActive));
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 10 : query.PageSize;

        var total = await doctors.CountAsync(cancellationToken);

        var items = await doctors
            .OrderByDescending(d => d.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DoctorProfile>(
            items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<DoctorProfile>> GetPendingAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorProfiles
            .Include(d => d.Specialization)
            .AsNoTracking()
            .Where(d => d.ApprovalStatus == DoctorApprovalStatus.Pending)
            .OrderBy(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(
        DoctorApprovalStatus? status = null,
        CancellationToken cancellationToken = default) =>
        status.HasValue
            ? _dbContext.DoctorProfiles.CountAsync(
                d => d.ApprovalStatus == status.Value, cancellationToken)
            : _dbContext.DoctorProfiles.CountAsync(cancellationToken);

    public async Task AddAsync(
        DoctorProfile profile,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorProfiles.AddAsync(
            profile, cancellationToken);

    public async Task<IReadOnlyList<DoctorPerformanceDto>> GetPerformanceAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var appointments = _dbContext.Appointments
            .Where(a => a.ScheduledAtUtc >= startDateUtc &&
                        a.ScheduledAtUtc < endDateUtc);

        var result = await _dbContext.DoctorProfiles
            .AsNoTracking()
            .Select(doctor => new DoctorPerformanceDto
            {
                DoctorId = doctor.Id,

                DoctorName = _dbContext.Users
                    .Where(u => u.Id == doctor.UserId)
                    .Select(u => u.FullName)
                    .FirstOrDefault() ?? "Unknown",

                TotalAppointments = appointments.Count(
                    a => a.DoctorProfileId == doctor.Id),

                CompletedAppointments = appointments.Count(
                    a => a.DoctorProfileId == doctor.Id &&
                         a.Status == AppointmentStatus.Completed),

                CancelledAppointments = appointments.Count(
                    a => a.DoctorProfileId == doctor.Id &&
                         a.Status == AppointmentStatus.Cancelled),

                NoShowAppointments = appointments.Count(
                    a => a.DoctorProfileId == doctor.Id &&
                         a.Status == AppointmentStatus.NoShow)
            })
            .ToListAsync(cancellationToken);

        foreach (var doctor in result)
        {
            doctor.CompletionRate = doctor.TotalAppointments == 0
                ? 0
                : Math.Round(
                    (double)doctor.CompletedAppointments /
                    doctor.TotalAppointments * 100, 2);
        }

        return result
            .OrderByDescending(d => d.TotalAppointments)
            .ToList();
    }
}