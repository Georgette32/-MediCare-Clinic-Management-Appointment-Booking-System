using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchedulingNotificationsAndDoctorDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConsultationFee",
                table: "SchedulingSettings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicDisplayName",
                table: "SchedulingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specialization",
                table: "SchedulingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SlotReleasedAt",
                table: "Offers",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RecurringEnd",
                table: "Breaks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RecurringStart",
                table: "Breaks",
                type: "time",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OfferId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentNotifications_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotifications_OfferId",
                table: "AppointmentNotifications",
                column: "OfferId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotifications_PatientId_ReadAt_CreatedAt",
                table: "AppointmentNotifications",
                columns: new[] { "PatientId", "ReadAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentNotifications");

            migrationBuilder.DropColumn(
                name: "ConsultationFee",
                table: "SchedulingSettings");

            migrationBuilder.DropColumn(
                name: "PublicDisplayName",
                table: "SchedulingSettings");

            migrationBuilder.DropColumn(
                name: "Specialization",
                table: "SchedulingSettings");

            migrationBuilder.DropColumn(
                name: "SlotReleasedAt",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "RecurringEnd",
                table: "Breaks");

            migrationBuilder.DropColumn(
                name: "RecurringStart",
                table: "Breaks");
        }
    }
}
