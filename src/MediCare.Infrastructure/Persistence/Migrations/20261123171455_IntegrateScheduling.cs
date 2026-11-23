using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrateScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorProfileId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_PatientProfileId",
                table: "Appointments");

            migrationBuilder.AddColumn<int>(
                name: "AppointmentDurationMinutes",
                table: "DoctorProfiles",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AppointmentDate",
                table: "Appointments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Appointments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "Appointments",
                type: "time(0)",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "FrozenGracePeriodMinutes",
                table: "Appointments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodStartedAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RescheduledFromAppointmentId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "Appointments",
                type: "time(0)",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            // Existing rows predate the scheduling workflow. Derive their slot from
            // ScheduledAtUtc so the new range check and slot indexes remain valid.
            migrationBuilder.Sql(@"
                UPDATE Appointments
                SET AppointmentDate = CAST(ScheduledAtUtc AS date),
                    StartTime = CAST(ScheduledAtUtc AS time(0)),
                    EndTime = CASE
                        WHEN DATEADD(minute, 30, CAST(ScheduledAtUtc AS time(0))) < CAST(ScheduledAtUtc AS time(0))
                            THEN CAST('23:59:59' AS time(0))
                        ELSE DATEADD(minute, 30, CAST(ScheduledAtUtc AS time(0)))
                    END
                WHERE AppointmentDate = '0001-01-01';");

            migrationBuilder.CreateTable(
                name: "DoctorBreaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorBreaks", x => x.Id);
                    table.CheckConstraint("CK_DoctorBreaks_TimeRange", "[EndTime] > [StartTime]");
                    table.ForeignKey(
                        name: "FK_DoctorBreaks_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorHolidays", x => x.Id);
                    table.CheckConstraint("CK_DoctorHolidays_DateRange", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_DoctorHolidays_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorWorkingHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorWorkingHours", x => x.Id);
                    table.CheckConstraint("CK_DoctorWorkingHours_TimeRange", "[EndTime] > [StartTime]");
                    table.ForeignKey(
                        name: "FK_DoctorWorkingHours_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QueueSequence = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CalledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsultationStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEntries", x => x.Id);
                    table.CheckConstraint("CK_QueueEntries_SourceAppointmentSequence", "([Source] = 1 AND [AppointmentId] IS NULL AND [QueueSequence] IS NOT NULL AND [QueueSequence] > 0) OR ([Source] = 0 AND [AppointmentId] IS NOT NULL AND [QueueSequence] IS NULL)");
                    table.ForeignKey(
                        name: "FK_QueueEntries_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueEntries_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueEntries_PatientProfiles_PatientProfileId",
                        column: x => x.PatientProfileId,
                        principalTable: "PatientProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalkInAdmissionApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QueueEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecisionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkInAdmissionApprovals", x => x.Id);
                    table.CheckConstraint("CK_WalkInAdmissionApprovals_DecisionTimestamp", "[Decision] IN (0, 1, 2, 3) AND (([Decision] = 0 AND [DecisionAtUtc] IS NULL AND [UsedAtUtc] IS NULL) OR ([Decision] = 1 AND [DecisionAtUtc] IS NOT NULL) OR ([Decision] IN (2, 3) AND [DecisionAtUtc] IS NOT NULL AND [UsedAtUtc] IS NULL)) AND ([UsedAtUtc] IS NULL OR [DecisionAtUtc] IS NULL OR [UsedAtUtc] >= [DecisionAtUtc])");
                    table.ForeignKey(
                        name: "FK_WalkInAdmissionApprovals_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalkInAdmissionApprovals_QueueEntries_QueueEntryId",
                        column: x => x.QueueEntryId,
                        principalTable: "QueueEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoctorProfiles_AppointmentDuration",
                table: "DoctorProfiles",
                sql: "[AppointmentDurationMinutes] BETWEEN 5 AND 240");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorProfileId_AppointmentDate_StartTime",
                table: "Appointments",
                columns: new[] { "DoctorProfileId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorProfileId_AppointmentDate_StartTime_EndTime",
                table: "Appointments",
                columns: new[] { "DoctorProfileId", "AppointmentDate", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientProfileId_AppointmentDate",
                table: "Appointments",
                columns: new[] { "PatientProfileId", "AppointmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RescheduledFromAppointmentId",
                table: "Appointments",
                column: "RescheduledFromAppointmentId",
                unique: true,
                filter: "[RescheduledFromAppointmentId] IS NOT NULL AND [Status] IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_GracePeriodPositive",
                table: "Appointments",
                sql: "[FrozenGracePeriodMinutes] IS NULL OR [FrozenGracePeriodMinutes] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_TimeRange",
                table: "Appointments",
                sql: "[EndTime] > [StartTime]");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorBreaks_DoctorProfileId_Date_StartTime",
                table: "DoctorBreaks",
                columns: new[] { "DoctorProfileId", "Date", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorHolidays_DoctorProfileId_StartDate_EndDate",
                table: "DoctorHolidays",
                columns: new[] { "DoctorProfileId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHours_DoctorProfileId_DayOfWeek_StartTime",
                table: "DoctorWorkingHours",
                columns: new[] { "DoctorProfileId", "DayOfWeek", "StartTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_AppointmentId",
                table: "QueueEntries",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_DoctorProfileId",
                table: "QueueEntries",
                column: "DoctorProfileId",
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_DoctorProfileId_QueueDate_QueueSequence",
                table: "QueueEntries",
                columns: new[] { "DoctorProfileId", "QueueDate", "QueueSequence" },
                unique: true,
                filter: "[QueueSequence] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_DoctorProfileId_Source_QueueDate_ArrivedAt_QueueSequence",
                table: "QueueEntries",
                columns: new[] { "DoctorProfileId", "Source", "QueueDate", "ArrivedAt", "QueueSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_PatientProfileId",
                table: "QueueEntries",
                column: "PatientProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_WalkInAdmissionApprovals_DoctorProfileId_Decision_RequestedAtUtc",
                table: "WalkInAdmissionApprovals",
                columns: new[] { "DoctorProfileId", "Decision", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalkInAdmissionApprovals_QueueEntryId",
                table: "WalkInAdmissionApprovals",
                column: "QueueEntryId",
                unique: true,
                filter: "[Decision] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Appointments_RescheduledFromAppointmentId",
                table: "Appointments",
                column: "RescheduledFromAppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Appointments_RescheduledFromAppointmentId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "DoctorBreaks");

            migrationBuilder.DropTable(
                name: "DoctorHolidays");

            migrationBuilder.DropTable(
                name: "DoctorWorkingHours");

            migrationBuilder.DropTable(
                name: "WalkInAdmissionApprovals");

            migrationBuilder.DropTable(
                name: "QueueEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DoctorProfiles_AppointmentDuration",
                table: "DoctorProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorProfileId_AppointmentDate_StartTime",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorProfileId_AppointmentDate_StartTime_EndTime",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_PatientProfileId_AppointmentDate",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RescheduledFromAppointmentId",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_GracePeriodPositive",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_TimeRange",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "AppointmentDurationMinutes",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "AppointmentDate",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FrozenGracePeriodMinutes",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "GracePeriodStartedAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RescheduledFromAppointmentId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorProfileId",
                table: "Appointments",
                column: "DoctorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientProfileId",
                table: "Appointments",
                column: "PatientProfileId");
        }
    }
}
