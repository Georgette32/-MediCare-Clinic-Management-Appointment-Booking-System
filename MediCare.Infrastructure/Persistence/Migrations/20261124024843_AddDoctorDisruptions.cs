using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorDisruptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DisruptionId",
                table: "DoctorBreaks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DoctorDisruptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FromTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DayEndRecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DayEndRecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorDisruptions", x => x.Id);
                    table.CheckConstraint("CK_DoctorDisruptions_DayEndAfterRecord", "[DayEndRecordedAtUtc] IS NULL OR [DayEndRecordedAtUtc] >= [RecordedAtUtc]");
                    table.CheckConstraint("CK_DoctorDisruptions_DayEndPair", "([DayEndRecordedAtUtc] IS NULL AND [DayEndRecordedByUserId] IS NULL) OR ([DayEndRecordedAtUtc] IS NOT NULL AND [DayEndRecordedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_DoctorDisruptions_ReasonRequired", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_DoctorDisruptions_AspNetUsers_DayEndRecordedByUserId",
                        column: x => x.DayEndRecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorDisruptions_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorDisruptions_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisruptionWorklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisruptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueueEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastContactedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastContactedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisruptionWorklistItems", x => x.Id);
                    table.CheckConstraint("CK_DisruptionWorklistItems_ContactPair", "([LastContactedByUserId] IS NULL AND [LastContactedAtUtc] IS NULL) OR ([LastContactedByUserId] IS NOT NULL AND [LastContactedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_DisruptionWorklistItems_Link", "[AppointmentId] IS NOT NULL OR [QueueEntryId] IS NOT NULL");
                    table.CheckConstraint("CK_DisruptionWorklistItems_Outcome", "[Outcome] IN ('Pending', 'Contacted', 'Rescheduled', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_DisruptionWorklistItems_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisruptionWorklistItems_AspNetUsers_LastContactedByUserId",
                        column: x => x.LastContactedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisruptionWorklistItems_DoctorDisruptions_DisruptionId",
                        column: x => x.DisruptionId,
                        principalTable: "DoctorDisruptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisruptionWorklistItems_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisruptionWorklistItems_QueueEntries_QueueEntryId",
                        column: x => x.QueueEntryId,
                        principalTable: "QueueEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisruptionContactAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorklistItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisruptionContactAttempts", x => x.Id);
                    table.CheckConstraint("CK_DisruptionContactAttempts_Outcome", "[Outcome] IN ('Pending', 'Contacted', 'Rescheduled', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_DisruptionContactAttempts_AspNetUsers_ContactedByUserId",
                        column: x => x.ContactedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisruptionContactAttempts_DisruptionWorklistItems_WorklistItemId",
                        column: x => x.WorklistItemId,
                        principalTable: "DisruptionWorklistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorBreaks_DisruptionId",
                table: "DoctorBreaks",
                column: "DisruptionId",
                filter: "[DisruptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionContactAttempts_ContactedByUserId",
                table: "DisruptionContactAttempts",
                column: "ContactedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionContactAttempts_WorklistItemId_ContactedAtUtc",
                table: "DisruptionContactAttempts",
                columns: new[] { "WorklistItemId", "ContactedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionWorklistItems_AppointmentId",
                table: "DisruptionWorklistItems",
                column: "AppointmentId",
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionWorklistItems_DisruptionId_AppointmentId",
                table: "DisruptionWorklistItems",
                columns: new[] { "DisruptionId", "AppointmentId" },
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionWorklistItems_DisruptionId_QueueEntryId",
                table: "DisruptionWorklistItems",
                columns: new[] { "DisruptionId", "QueueEntryId" },
                unique: true,
                filter: "[QueueEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionWorklistItems_LastContactedByUserId",
                table: "DisruptionWorklistItems",
                column: "LastContactedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionWorklistItems_PatientId",
                table: "DisruptionWorklistItems",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_DisruptionWorklistItems_QueueEntryId",
                table: "DisruptionWorklistItems",
                column: "QueueEntryId",
                filter: "[QueueEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorDisruptions_DayEndRecordedByUserId",
                table: "DoctorDisruptions",
                column: "DayEndRecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorDisruptions_DoctorId_Date",
                table: "DoctorDisruptions",
                columns: new[] { "DoctorId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorDisruptions_RecordedAtUtc",
                table: "DoctorDisruptions",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorDisruptions_RecordedByUserId",
                table: "DoctorDisruptions",
                column: "RecordedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DoctorBreaks_DoctorDisruptions_DisruptionId",
                table: "DoctorBreaks",
                column: "DisruptionId",
                principalTable: "DoctorDisruptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DoctorBreaks_DoctorDisruptions_DisruptionId",
                table: "DoctorBreaks");

            migrationBuilder.DropTable(
                name: "DisruptionContactAttempts");

            migrationBuilder.DropTable(
                name: "DisruptionWorklistItems");

            migrationBuilder.DropTable(
                name: "DoctorDisruptions");

            migrationBuilder.DropIndex(
                name: "IX_DoctorBreaks_DisruptionId",
                table: "DoctorBreaks");

            migrationBuilder.DropColumn(
                name: "DisruptionId",
                table: "DoctorBreaks");
        }
    }
}
