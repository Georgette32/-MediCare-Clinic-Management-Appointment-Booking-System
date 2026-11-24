using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorWorkingHourExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorWorkingHourExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OriginalEndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    ExtendedEndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorWorkingHourExtensions", x => x.Id);
                    table.CheckConstraint("CK_DoctorWorkingHourExtensions_DecidedAfterRequest", "[DecidedAtUtc] IS NULL OR [DecidedAtUtc] >= [RequestedAtUtc]");
                    table.CheckConstraint("CK_DoctorWorkingHourExtensions_DecisionTimestamp", "([Status] = 'Pending' AND [DecidedAtUtc] IS NULL) OR ([Status] IN ('Approved', 'Rejected', 'Cancelled') AND [DecidedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_DoctorWorkingHourExtensions_Status", "[Status] IN ('Pending', 'Approved', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("CK_DoctorWorkingHourExtensions_TimeRange", "[ExtendedEndTime] > [OriginalEndTime]");
                    table.ForeignKey(
                        name: "FK_DoctorWorkingHourExtensions_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorWorkingHourExtensions_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHourExtensions_DoctorId_Date",
                table: "DoctorWorkingHourExtensions",
                columns: new[] { "DoctorId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHourExtensions_DoctorId_Date_OriginalEndTime",
                table: "DoctorWorkingHourExtensions",
                columns: new[] { "DoctorId", "Date", "OriginalEndTime" },
                unique: true,
                filter: "[Status] IN ('Pending', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHourExtensions_RequestedByUserId",
                table: "DoctorWorkingHourExtensions",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorWorkingHourExtensions");
        }
    }
}
