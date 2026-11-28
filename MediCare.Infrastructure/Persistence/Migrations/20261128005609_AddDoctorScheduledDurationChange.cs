using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorScheduledDurationChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextAppointmentDurationMinutes",
                table: "Doctors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextDurationEffectiveFrom",
                table: "Doctors",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Doctors_NextDuration",
                table: "Doctors",
                sql: "([NextAppointmentDurationMinutes] IS NULL AND [NextDurationEffectiveFrom] IS NULL) OR ([NextAppointmentDurationMinutes] IS NOT NULL AND [NextDurationEffectiveFrom] IS NOT NULL AND [NextAppointmentDurationMinutes] > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Doctors_NextDuration",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "NextAppointmentDurationMinutes",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "NextDurationEffectiveFrom",
                table: "Doctors");
        }
    }
}
