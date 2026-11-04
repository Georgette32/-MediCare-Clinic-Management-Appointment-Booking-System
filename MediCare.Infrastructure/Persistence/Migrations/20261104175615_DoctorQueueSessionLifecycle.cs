using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DoctorQueueSessionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DoctorQueueSessionId",
                table: "QueueEntries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DoctorQueueSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DoctorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorQueueSessions", x => x.Id);
                    table.UniqueConstraint("AK_DoctorQueueSessions_DoctorId_Id", x => new { x.DoctorId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_DoctorId_DoctorQueueSessionId",
                table: "QueueEntries",
                columns: new[] { "DoctorId", "DoctorQueueSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorQueueSessions_DoctorId_Active",
                table: "DoctorQueueSessions",
                columns: new[] { "DoctorId", "Status" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorQueueSessions_DoctorId_CreatedAt",
                table: "DoctorQueueSessions",
                columns: new[] { "DoctorId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_QueueEntries_DoctorQueueSessions_DoctorId_DoctorQueueSessionId",
                table: "QueueEntries",
                columns: new[] { "DoctorId", "DoctorQueueSessionId" },
                principalTable: "DoctorQueueSessions",
                principalColumns: new[] { "DoctorId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QueueEntries_DoctorQueueSessions_DoctorId_DoctorQueueSessionId",
                table: "QueueEntries");

            migrationBuilder.DropTable(
                name: "DoctorQueueSessions");

            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_DoctorId_DoctorQueueSessionId",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DoctorQueueSessionId",
                table: "QueueEntries");
        }
    }
}
