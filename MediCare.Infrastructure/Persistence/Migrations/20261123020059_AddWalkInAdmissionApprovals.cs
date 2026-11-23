using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkInAdmissionApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalkInAdmissionApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QueueEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecisionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkInAdmissionApprovals", x => x.Id);
                    table.CheckConstraint("CK_WalkInAdmissionApprovals_DecisionTimestamp", "[Decision] IN ('Pending', 'Approved', 'Rejected', 'Expired') AND (([Decision] = 'Pending' AND [DecisionAtUtc] IS NULL AND [UsedAtUtc] IS NULL) OR ([Decision] = 'Approved' AND [DecisionAtUtc] IS NOT NULL) OR ([Decision] IN ('Rejected', 'Expired') AND [DecisionAtUtc] IS NOT NULL AND [UsedAtUtc] IS NULL)) AND ([UsedAtUtc] IS NULL OR [DecisionAtUtc] IS NULL OR [UsedAtUtc] >= [DecisionAtUtc])");
                    table.ForeignKey(
                        name: "FK_WalkInAdmissionApprovals_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalkInAdmissionApprovals_QueueEntries_QueueEntryId",
                        column: x => x.QueueEntryId,
                        principalTable: "QueueEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalkInAdmissionApprovals_DoctorId_Decision_RequestedAtUtc",
                table: "WalkInAdmissionApprovals",
                columns: new[] { "DoctorId", "Decision", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalkInAdmissionApprovals_QueueEntryId",
                table: "WalkInAdmissionApprovals",
                column: "QueueEntryId",
                unique: true,
                filter: "[Decision] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalkInAdmissionApprovals");
        }
    }
}
