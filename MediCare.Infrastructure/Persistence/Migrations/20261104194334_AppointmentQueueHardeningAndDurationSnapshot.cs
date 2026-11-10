using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentQueueHardeningAndDurationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentNotifications_OfferId",
                table: "AppointmentNotifications");

            migrationBuilder.AddColumn<long>(
                name: "DoctorQueueSessionId",
                table: "Events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSnapshotMinutes",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Appointments SET DurationSnapshotMinutes = DATEDIFF(MINUTE, ScheduledStart, ScheduledEnd) WHERE DurationSnapshotMinutes = 0 AND ScheduledEnd > ScheduledStart");

            migrationBuilder.AlterColumn<long>(
                name: "OfferId",
                table: "AppointmentNotifications",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateIndex(
                name: "IX_Events_DoctorId_DoctorQueueSessionId",
                table: "Events",
                columns: new[] { "DoctorId", "DoctorQueueSessionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoctorQueueSession_State",
                table: "DoctorQueueSessions",
                sql: "([Status] = 0 AND [StartedAt] IS NULL AND [ClosedAt] IS NULL) OR ([Status] = 1 AND [StartedAt] IS NOT NULL AND [ClosedAt] IS NULL) OR ([Status] = 2 AND [StartedAt] IS NOT NULL AND [ClosedAt] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointment_DurationSnapshot",
                table: "Appointments",
                sql: "[DurationSnapshotMinutes] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotifications_OfferId",
                table: "AppointmentNotifications",
                column: "OfferId",
                unique: true,
                filter: "[OfferId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_DoctorQueueSessions_DoctorId_DoctorQueueSessionId",
                table: "Events",
                columns: new[] { "DoctorId", "DoctorQueueSessionId" },
                principalTable: "DoctorQueueSessions",
                principalColumns: new[] { "DoctorId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_DoctorQueueSessions_DoctorId_DoctorQueueSessionId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_DoctorId_DoctorQueueSessionId",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DoctorQueueSession_State",
                table: "DoctorQueueSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointment_DurationSnapshot",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentNotifications_OfferId",
                table: "AppointmentNotifications");

            migrationBuilder.DropColumn(
                name: "DoctorQueueSessionId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DurationSnapshotMinutes",
                table: "Appointments");

            migrationBuilder.AlterColumn<long>(
                name: "OfferId",
                table: "AppointmentNotifications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotifications_OfferId",
                table: "AppointmentNotifications",
                column: "OfferId",
                unique: true);
        }
    }
}
