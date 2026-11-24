using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "DisruptionWorklistItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAtUtc",
                table: "DisruptionWorklistItems",
                type: "datetimeoffset",
                nullable: true);

            // Existing closed items (Rescheduled or Cancelled) become resolved at their last update, so the new
            // check constraints hold for historical rows.
            migrationBuilder.Sql(
                "UPDATE [DisruptionWorklistItems] SET [ResolvedAtUtc] = [UpdatedAtUtc] WHERE [Outcome] IN ('Rescheduled', 'Cancelled') AND [ResolvedAtUtc] IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DisruptionWorklistItems_ClosedOutcomeResolved",
                table: "DisruptionWorklistItems",
                sql: "[Outcome] NOT IN ('Rescheduled', 'Cancelled') OR [ResolvedAtUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DisruptionWorklistItems_ResolutionNote",
                table: "DisruptionWorklistItems",
                sql: "[ResolvedAtUtc] IS NULL OR [Outcome] IN ('Rescheduled', 'Cancelled') OR [ResolutionNote] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DisruptionWorklistItems_ClosedOutcomeResolved",
                table: "DisruptionWorklistItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DisruptionWorklistItems_ResolutionNote",
                table: "DisruptionWorklistItems");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "DisruptionWorklistItems");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "DisruptionWorklistItems");
        }
    }
}
