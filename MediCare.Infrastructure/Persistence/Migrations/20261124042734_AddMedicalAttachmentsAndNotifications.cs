using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalAttachmentsAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MedicalAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByDoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalAttachments", x => x.Id);
                    table.CheckConstraint("CK_MedicalAttachments_ContentType", "[ContentType] IN ('application/pdf', 'image/jpeg', 'image/png')");
                    table.CheckConstraint("CK_MedicalAttachments_DescriptionNotBlank", "[Description] IS NULL OR LEN(LTRIM(RTRIM([Description]))) > 0");
                    table.CheckConstraint("CK_MedicalAttachments_OriginalFileNameRequired", "LEN(LTRIM(RTRIM([OriginalFileName]))) > 0");
                    table.CheckConstraint("CK_MedicalAttachments_Sha256Length", "LEN([Sha256]) = 64");
                    table.CheckConstraint("CK_MedicalAttachments_SizePositive", "[SizeBytes] > 0");
                    table.CheckConstraint("CK_MedicalAttachments_StoredFileNameLength", "LEN([StoredFileName]) = 36");
                    table.ForeignKey(
                        name: "FK_MedicalAttachments_Doctors_UploadedByDoctorId",
                        column: x => x.UploadedByDoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalAttachments_MedicalRecords_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "MedicalRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DedupKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_DedupKeyNotBlank", "[DedupKey] IS NULL OR LEN(LTRIM(RTRIM([DedupKey]))) > 0");
                    table.CheckConstraint("CK_Notifications_KindRequired", "LEN(LTRIM(RTRIM([Kind]))) > 0");
                    table.CheckConstraint("CK_Notifications_MessageRequired", "LEN(LTRIM(RTRIM([Message]))) > 0");
                    table.CheckConstraint("CK_Notifications_RelatedPair", "([RelatedEntityType] IS NULL AND [RelatedEntityId] IS NULL) OR ([RelatedEntityType] IS NOT NULL AND [RelatedEntityId] IS NOT NULL)");
                    table.CheckConstraint("CK_Notifications_TitleRequired", "LEN(LTRIM(RTRIM([Title]))) > 0");
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAttachments_MedicalRecordId_UploadedAtUtc",
                table: "MedicalAttachments",
                columns: new[] { "MedicalRecordId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAttachments_StoredFileName",
                table: "MedicalAttachments",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAttachments_UploadedByDoctorId",
                table: "MedicalAttachments",
                column: "UploadedByDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_DedupKey",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "DedupKey" },
                unique: true,
                filter: "[DedupKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "ReadAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicalAttachments");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
