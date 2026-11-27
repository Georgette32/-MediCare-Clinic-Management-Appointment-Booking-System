using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueEntryClosedAtAndWorkingHourSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAtUtc",
                table: "QueueEntries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DoctorWorkingHourSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkingHourId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorWorkingHourSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorWorkingHourSnapshots_DoctorWorkingHours_WorkingHourId",
                        column: x => x.WorkingHourId,
                        principalTable: "DoctorWorkingHours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorWorkingHourSnapshots_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHourSnapshots_DoctorId_EffectiveFrom",
                table: "DoctorWorkingHourSnapshots",
                columns: new[] { "DoctorId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorWorkingHourSnapshots_WorkingHourId",
                table: "DoctorWorkingHourSnapshots",
                column: "WorkingHourId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorWorkingHourSnapshots");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "QueueEntries");
        }
    }
}
