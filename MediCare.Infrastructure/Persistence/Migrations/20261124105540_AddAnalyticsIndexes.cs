using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_QueueDate_DoctorId",
                table: "QueueEntries",
                columns: new[] { "QueueDate", "DoctorId" })
                .Annotation("SqlServer:Include", new[] { "Status", "Source", "PatientId", "AppointmentId", "ArrivedAt", "CalledAt", "ConsultationStartedAt", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_QueueDate_DoctorId",
                table: "QueueEntries");
        }
    }
}
