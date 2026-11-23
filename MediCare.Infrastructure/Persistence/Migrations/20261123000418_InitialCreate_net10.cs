using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate_net10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FrozenGracePeriodMinutes",
                table: "Appointments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GracePeriodStartedAtUtc",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_GracePeriodPositive",
                table: "Appointments",
                sql: "[FrozenGracePeriodMinutes] IS NULL OR [FrozenGracePeriodMinutes] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_GracePeriodPositive",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FrozenGracePeriodMinutes",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "GracePeriodStartedAtUtc",
                table: "Appointments");
        }
    }
}
