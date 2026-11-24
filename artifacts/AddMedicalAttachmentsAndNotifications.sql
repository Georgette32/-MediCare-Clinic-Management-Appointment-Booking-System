BEGIN TRANSACTION;
CREATE TABLE [MedicalAttachments] (
    [Id] uniqueidentifier NOT NULL,
    [MedicalRecordId] uniqueidentifier NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [ContentType] nvarchar(32) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [StoredFileName] nvarchar(36) NOT NULL,
    [Sha256] nvarchar(64) NOT NULL,
    [Description] nvarchar(500) NULL,
    [UploadedByDoctorId] uniqueidentifier NOT NULL,
    [UploadedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_MedicalAttachments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MedicalAttachments_ContentType] CHECK ([ContentType] IN ('application/pdf', 'image/jpeg', 'image/png')),
    CONSTRAINT [CK_MedicalAttachments_DescriptionNotBlank] CHECK ([Description] IS NULL OR LEN(LTRIM(RTRIM([Description]))) > 0),
    CONSTRAINT [CK_MedicalAttachments_OriginalFileNameRequired] CHECK (LEN(LTRIM(RTRIM([OriginalFileName]))) > 0),
    CONSTRAINT [CK_MedicalAttachments_Sha256Length] CHECK (LEN([Sha256]) = 64),
    CONSTRAINT [CK_MedicalAttachments_SizePositive] CHECK ([SizeBytes] > 0),
    CONSTRAINT [CK_MedicalAttachments_StoredFileNameLength] CHECK (LEN([StoredFileName]) = 36),
    CONSTRAINT [FK_MedicalAttachments_Doctors_UploadedByDoctorId] FOREIGN KEY ([UploadedByDoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MedicalAttachments_MedicalRecords_MedicalRecordId] FOREIGN KEY ([MedicalRecordId]) REFERENCES [MedicalRecords] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Notifications] (
    [Id] uniqueidentifier NOT NULL,
    [RecipientUserId] uniqueidentifier NOT NULL,
    [Kind] nvarchar(48) NOT NULL,
    [Title] nvarchar(150) NOT NULL,
    [Message] nvarchar(1000) NOT NULL,
    [RelatedEntityType] nvarchar(48) NULL,
    [RelatedEntityId] uniqueidentifier NULL,
    [DedupKey] nvarchar(150) NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [ReadAtUtc] datetimeoffset NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Notifications_DedupKeyNotBlank] CHECK ([DedupKey] IS NULL OR LEN(LTRIM(RTRIM([DedupKey]))) > 0),
    CONSTRAINT [CK_Notifications_KindRequired] CHECK (LEN(LTRIM(RTRIM([Kind]))) > 0),
    CONSTRAINT [CK_Notifications_MessageRequired] CHECK (LEN(LTRIM(RTRIM([Message]))) > 0),
    CONSTRAINT [CK_Notifications_RelatedPair] CHECK (([RelatedEntityType] IS NULL AND [RelatedEntityId] IS NULL) OR ([RelatedEntityType] IS NOT NULL AND [RelatedEntityId] IS NOT NULL)),
    CONSTRAINT [CK_Notifications_TitleRequired] CHECK (LEN(LTRIM(RTRIM([Title]))) > 0),
    CONSTRAINT [FK_Notifications_AspNetUsers_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_MedicalAttachments_MedicalRecordId_UploadedAtUtc] ON [MedicalAttachments] ([MedicalRecordId], [UploadedAtUtc]);

CREATE UNIQUE INDEX [IX_MedicalAttachments_StoredFileName] ON [MedicalAttachments] ([StoredFileName]);

CREATE INDEX [IX_MedicalAttachments_UploadedByDoctorId] ON [MedicalAttachments] ([UploadedByDoctorId]);

CREATE UNIQUE INDEX [IX_Notifications_RecipientUserId_DedupKey] ON [Notifications] ([RecipientUserId], [DedupKey]) WHERE [DedupKey] IS NOT NULL;

CREATE INDEX [IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc] ON [Notifications] ([RecipientUserId], [ReadAtUtc], [CreatedAtUtc]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124042734_AddMedicalAttachmentsAndNotifications', N'10.0.0');

COMMIT;
GO

