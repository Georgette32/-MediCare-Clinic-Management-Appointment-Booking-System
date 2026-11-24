using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleManagementAndClinicSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Biography",
                table: "Doctors",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MinimumConsultationSampleSize = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSettings", x => x.Id);
                    table.CheckConstraint("CK_ClinicSettings_MinimumSampleSizePositive", "[MinimumConsultationSampleSize] IS NULL OR [MinimumConsultationSampleSize] > 0");
                    table.CheckConstraint("CK_ClinicSettings_SingletonId", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_ClinicSettings_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSettings_UpdatedByUserId",
                table: "ClinicSettings",
                column: "UpdatedByUserId");

            // Overlapping ACTIVE working windows for the same doctor and weekday are refused by the database.
            // A check constraint cannot compare rows, so an AFTER INSERT/UPDATE trigger performs the comparison.
            // CREATE TRIGGER must be the first statement of its batch, and the generated migration script puts every
            // statement in one batch, so the trigger is created through sp_executesql (its own batch).
            const string triggerDefinition = """
                CREATE TRIGGER [TR_DoctorWorkingHours_NoActiveOverlap]
                ON [DoctorWorkingHours]
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM [inserted] AS [i]
                        INNER JOIN [DoctorWorkingHours] AS [o]
                            ON [o].[DoctorId] = [i].[DoctorId]
                           AND [o].[DayOfWeek] = [i].[DayOfWeek]
                           AND [o].[Id] <> [i].[Id]
                        WHERE [i].[IsActive] = 1
                          AND [o].[IsActive] = 1
                          AND [o].[StartTime] < [i].[EndTime]
                          AND [o].[EndTime] > [i].[StartTime])
                    BEGIN
                        THROW 50001, 'Overlapping active working-hour windows are not allowed for the same doctor and weekday.', 1;
                    END
                END
                """;
            migrationBuilder.Sql("EXEC sp_executesql N'" + triggerDefinition.Replace("'", "''") + "';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_DoctorWorkingHours_NoActiveOverlap];");

            migrationBuilder.DropTable(
                name: "ClinicSettings");

            migrationBuilder.DropColumn(
                name: "Biography",
                table: "Doctors");
        }
    }
}
