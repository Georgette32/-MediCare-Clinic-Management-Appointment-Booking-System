using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261121190210_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AspNetRoles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                AccessFailedCount = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUsers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Specializations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Specializations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AspNetRoleClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "AspNetRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserRoles",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "AspNetRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserTokens",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Patients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApplicationUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Patients", x => x.Id);
                table.ForeignKey(
                    name: "FK_Patients_AspNetUsers_ApplicationUserId",
                    column: x => x.ApplicationUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Doctors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApplicationUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                SpecializationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AppointmentDurationMinutes = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Doctors", x => x.Id);
                table.CheckConstraint("CK_Doctors_AppointmentDurationPositive", "[AppointmentDurationMinutes] > 0");
                table.ForeignKey(
                    name: "FK_Doctors_AspNetUsers_ApplicationUserId",
                    column: x => x.ApplicationUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Doctors_Specializations_SpecializationId",
                    column: x => x.SpecializationId,
                    principalTable: "Specializations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Appointments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RescheduledFromAppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AppointmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Appointments", x => x.Id);
                table.CheckConstraint("CK_Appointments_TimeRange", "[EndTime] > [StartTime]");
                table.ForeignKey(
                    name: "FK_Appointments_Appointments_RescheduledFromAppointmentId",
                    column: x => x.RescheduledFromAppointmentId,
                    principalTable: "Appointments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Appointments_Doctors_DoctorId",
                    column: x => x.DoctorId,
                    principalTable: "Doctors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Appointments_Patients_PatientId",
                    column: x => x.PatientId,
                    principalTable: "Patients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DoctorBreaks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                EndTime = table.Column<TimeOnly>(type: "time", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DoctorBreaks", x => x.Id);
                table.CheckConstraint("CK_DoctorBreaks_TimeRange", "[EndTime] > [StartTime]");
                table.ForeignKey(
                    name: "FK_DoctorBreaks_Doctors_DoctorId",
                    column: x => x.DoctorId,
                    principalTable: "Doctors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DoctorHolidays",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DoctorHolidays", x => x.Id);
                table.CheckConstraint("CK_DoctorHolidays_DateRange", "[EndDate] >= [StartDate]");
                table.ForeignKey(
                    name: "FK_DoctorHolidays_Doctors_DoctorId",
                    column: x => x.DoctorId,
                    principalTable: "Doctors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DoctorWorkingHours",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DayOfWeek = table.Column<int>(type: "int", nullable: false),
                StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DoctorWorkingHours", x => x.Id);
                table.CheckConstraint("CK_DoctorWorkingHours_TimeRange", "[EndTime] > [StartTime]");
                table.ForeignKey(
                    name: "FK_DoctorWorkingHours_Doctors_DoctorId",
                    column: x => x.DoctorId,
                    principalTable: "Doctors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "QueueEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                QueueDate = table.Column<DateOnly>(type: "date", nullable: false),
                Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ArrivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                QueueSequence = table.Column<long>(type: "bigint", nullable: true),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                CalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ConsultationStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_QueueEntries", x => x.Id);
                table.CheckConstraint("CK_QueueEntries_SourceAppointmentSequence", "([Source] = 'WalkIn' AND [AppointmentId] IS NULL AND [QueueSequence] IS NOT NULL AND [QueueSequence] > 0) OR ([Source] = 'Scheduled' AND [AppointmentId] IS NOT NULL AND [QueueSequence] IS NULL)");
                table.ForeignKey(
                    name: "FK_QueueEntries_Appointments_AppointmentId",
                    column: x => x.AppointmentId,
                    principalTable: "Appointments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_QueueEntries_Doctors_DoctorId",
                    column: x => x.DoctorId,
                    principalTable: "Doctors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_QueueEntries_Patients_PatientId",
                    column: x => x.PatientId,
                    principalTable: "Patients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Appointments_DoctorId_AppointmentDate_StartTime",
            table: "Appointments",
            columns: new[] { "DoctorId", "AppointmentDate", "StartTime" },
            unique: true,
            filter: "[Status] IN ('Pending', 'Confirmed')");

        migrationBuilder.CreateIndex(
            name: "IX_Appointments_DoctorId_AppointmentDate_StartTime_EndTime",
            table: "Appointments",
            columns: new[] { "DoctorId", "AppointmentDate", "StartTime", "EndTime" });

        migrationBuilder.CreateIndex(
            name: "IX_Appointments_PatientId_AppointmentDate",
            table: "Appointments",
            columns: new[] { "PatientId", "AppointmentDate" });

        migrationBuilder.CreateIndex(
            name: "IX_Appointments_RescheduledFromAppointmentId",
            table: "Appointments",
            column: "RescheduledFromAppointmentId",
            unique: true,
            filter: "[RescheduledFromAppointmentId] IS NOT NULL AND [Status] IN ('Pending', 'Confirmed')");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetRoleClaims_RoleId",
            table: "AspNetRoleClaims",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            table: "AspNetRoles",
            column: "NormalizedName",
            unique: true,
            filter: "[NormalizedName] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserClaims_UserId",
            table: "AspNetUserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserLogins_UserId",
            table: "AspNetUserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserRoles_RoleId",
            table: "AspNetUserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "AspNetUsers",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "AspNetUsers",
            column: "NormalizedUserName",
            unique: true,
            filter: "[NormalizedUserName] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_DoctorBreaks_DoctorId_Date_StartTime",
            table: "DoctorBreaks",
            columns: new[] { "DoctorId", "Date", "StartTime" });

        migrationBuilder.CreateIndex(
            name: "IX_DoctorHolidays_DoctorId_StartDate_EndDate",
            table: "DoctorHolidays",
            columns: new[] { "DoctorId", "StartDate", "EndDate" });

        migrationBuilder.CreateIndex(
            name: "IX_Doctors_ApplicationUserId",
            table: "Doctors",
            column: "ApplicationUserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Doctors_SpecializationId",
            table: "Doctors",
            column: "SpecializationId");

        migrationBuilder.CreateIndex(
            name: "IX_DoctorWorkingHours_DoctorId_DayOfWeek_StartTime",
            table: "DoctorWorkingHours",
            columns: new[] { "DoctorId", "DayOfWeek", "StartTime" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Patients_ApplicationUserId",
            table: "Patients",
            column: "ApplicationUserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_QueueEntries_AppointmentId",
            table: "QueueEntries",
            column: "AppointmentId",
            unique: true,
            filter: "[AppointmentId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_QueueEntries_DoctorId",
            table: "QueueEntries",
            column: "DoctorId",
            unique: true,
            filter: "[Status] = 'InConsultation'");

        migrationBuilder.CreateIndex(
            name: "IX_QueueEntries_DoctorId_QueueDate_QueueSequence",
            table: "QueueEntries",
            columns: new[] { "DoctorId", "QueueDate", "QueueSequence" },
            unique: true,
            filter: "[QueueSequence] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_QueueEntries_DoctorId_Source_QueueDate_ArrivedAt_QueueSequence",
            table: "QueueEntries",
            columns: new[] { "DoctorId", "Source", "QueueDate", "ArrivedAt", "QueueSequence" });

        migrationBuilder.CreateIndex(
            name: "IX_QueueEntries_PatientId",
            table: "QueueEntries",
            column: "PatientId");

        migrationBuilder.CreateIndex(
            name: "IX_Specializations_Name",
            table: "Specializations",
            column: "Name",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AspNetRoleClaims");

        migrationBuilder.DropTable(
            name: "AspNetUserClaims");

        migrationBuilder.DropTable(
            name: "AspNetUserLogins");

        migrationBuilder.DropTable(
            name: "AspNetUserRoles");

        migrationBuilder.DropTable(
            name: "AspNetUserTokens");

        migrationBuilder.DropTable(
            name: "DoctorBreaks");

        migrationBuilder.DropTable(
            name: "DoctorHolidays");

        migrationBuilder.DropTable(
            name: "DoctorWorkingHours");

        migrationBuilder.DropTable(
            name: "QueueEntries");

        migrationBuilder.DropTable(
            name: "AspNetRoles");

        migrationBuilder.DropTable(
            name: "Appointments");

        migrationBuilder.DropTable(
            name: "Doctors");

        migrationBuilder.DropTable(
            name: "Patients");

        migrationBuilder.DropTable(
            name: "Specializations");

        migrationBuilder.DropTable(
            name: "AspNetUsers");
    }
}
