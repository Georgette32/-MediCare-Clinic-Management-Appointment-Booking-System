using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailCodeExpiresAtUtc",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailCodeFailedAttempts",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailCodeHash",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailCodeSentAtUtc",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresEmailVerification",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailCodeExpiresAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailCodeFailedAttempts",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailCodeHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailCodeSentAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RequiresEmailVerification",
                table: "AspNetUsers");
        }
    }
}
