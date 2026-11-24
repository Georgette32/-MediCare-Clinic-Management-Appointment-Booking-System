BEGIN TRANSACTION;
ALTER TABLE [DoctorBreaks] ADD [DisruptionId] uniqueidentifier NULL;

CREATE TABLE [DoctorDisruptions] (
    [Id] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [Date] date NOT NULL,
    [FromTime] time NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    [RecordedByUserId] uniqueidentifier NOT NULL,
    [RecordedAtUtc] datetimeoffset NOT NULL,
    [DayEndRecordedAtUtc] datetimeoffset NULL,
    [DayEndRecordedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_DoctorDisruptions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DoctorDisruptions_DayEndAfterRecord] CHECK ([DayEndRecordedAtUtc] IS NULL OR [DayEndRecordedAtUtc] >= [RecordedAtUtc]),
    CONSTRAINT [CK_DoctorDisruptions_DayEndPair] CHECK (([DayEndRecordedAtUtc] IS NULL AND [DayEndRecordedByUserId] IS NULL) OR ([DayEndRecordedAtUtc] IS NOT NULL AND [DayEndRecordedByUserId] IS NOT NULL)),
    CONSTRAINT [CK_DoctorDisruptions_ReasonRequired] CHECK (LEN(LTRIM(RTRIM([Reason]))) > 0),
    CONSTRAINT [FK_DoctorDisruptions_AspNetUsers_DayEndRecordedByUserId] FOREIGN KEY ([DayEndRecordedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DoctorDisruptions_AspNetUsers_RecordedByUserId] FOREIGN KEY ([RecordedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DoctorDisruptions_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [DisruptionWorklistItems] (
    [Id] uniqueidentifier NOT NULL,
    [DisruptionId] uniqueidentifier NOT NULL,
    [PatientId] uniqueidentifier NOT NULL,
    [AppointmentId] uniqueidentifier NULL,
    [QueueEntryId] uniqueidentifier NULL,
    [Outcome] nvarchar(16) NOT NULL,
    [LastContactedByUserId] uniqueidentifier NULL,
    [LastContactedAtUtc] datetimeoffset NULL,
    [Notes] nvarchar(1000) NULL,
    [UpdatedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_DisruptionWorklistItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DisruptionWorklistItems_ContactPair] CHECK (([LastContactedByUserId] IS NULL AND [LastContactedAtUtc] IS NULL) OR ([LastContactedByUserId] IS NOT NULL AND [LastContactedAtUtc] IS NOT NULL)),
    CONSTRAINT [CK_DisruptionWorklistItems_Link] CHECK ([AppointmentId] IS NOT NULL OR [QueueEntryId] IS NOT NULL),
    CONSTRAINT [CK_DisruptionWorklistItems_Outcome] CHECK ([Outcome] IN ('Pending', 'Contacted', 'Rescheduled', 'Cancelled')),
    CONSTRAINT [FK_DisruptionWorklistItems_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisruptionWorklistItems_AspNetUsers_LastContactedByUserId] FOREIGN KEY ([LastContactedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisruptionWorklistItems_DoctorDisruptions_DisruptionId] FOREIGN KEY ([DisruptionId]) REFERENCES [DoctorDisruptions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisruptionWorklistItems_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisruptionWorklistItems_QueueEntries_QueueEntryId] FOREIGN KEY ([QueueEntryId]) REFERENCES [QueueEntries] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [DisruptionContactAttempts] (
    [Id] uniqueidentifier NOT NULL,
    [WorklistItemId] uniqueidentifier NOT NULL,
    [ContactedByUserId] uniqueidentifier NOT NULL,
    [ContactedAtUtc] datetimeoffset NOT NULL,
    [Notes] nvarchar(1000) NULL,
    [Outcome] nvarchar(16) NOT NULL,
    CONSTRAINT [PK_DisruptionContactAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DisruptionContactAttempts_Outcome] CHECK ([Outcome] IN ('Pending', 'Contacted', 'Rescheduled', 'Cancelled')),
    CONSTRAINT [FK_DisruptionContactAttempts_AspNetUsers_ContactedByUserId] FOREIGN KEY ([ContactedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisruptionContactAttempts_DisruptionWorklistItems_WorklistItemId] FOREIGN KEY ([WorklistItemId]) REFERENCES [DisruptionWorklistItems] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_DoctorBreaks_DisruptionId] ON [DoctorBreaks] ([DisruptionId]) WHERE [DisruptionId] IS NOT NULL;

CREATE INDEX [IX_DisruptionContactAttempts_ContactedByUserId] ON [DisruptionContactAttempts] ([ContactedByUserId]);

CREATE INDEX [IX_DisruptionContactAttempts_WorklistItemId_ContactedAtUtc] ON [DisruptionContactAttempts] ([WorklistItemId], [ContactedAtUtc]);

CREATE INDEX [IX_DisruptionWorklistItems_AppointmentId] ON [DisruptionWorklistItems] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_DisruptionWorklistItems_DisruptionId_AppointmentId] ON [DisruptionWorklistItems] ([DisruptionId], [AppointmentId]) WHERE [AppointmentId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_DisruptionWorklistItems_DisruptionId_QueueEntryId] ON [DisruptionWorklistItems] ([DisruptionId], [QueueEntryId]) WHERE [QueueEntryId] IS NOT NULL;

CREATE INDEX [IX_DisruptionWorklistItems_LastContactedByUserId] ON [DisruptionWorklistItems] ([LastContactedByUserId]);

CREATE INDEX [IX_DisruptionWorklistItems_PatientId] ON [DisruptionWorklistItems] ([PatientId]);

CREATE INDEX [IX_DisruptionWorklistItems_QueueEntryId] ON [DisruptionWorklistItems] ([QueueEntryId]) WHERE [QueueEntryId] IS NOT NULL;

CREATE INDEX [IX_DoctorDisruptions_DayEndRecordedByUserId] ON [DoctorDisruptions] ([DayEndRecordedByUserId]);

CREATE UNIQUE INDEX [IX_DoctorDisruptions_DoctorId_Date] ON [DoctorDisruptions] ([DoctorId], [Date]);

CREATE INDEX [IX_DoctorDisruptions_RecordedAtUtc] ON [DoctorDisruptions] ([RecordedAtUtc]);

CREATE INDEX [IX_DoctorDisruptions_RecordedByUserId] ON [DoctorDisruptions] ([RecordedByUserId]);

ALTER TABLE [DoctorBreaks] ADD CONSTRAINT [FK_DoctorBreaks_DoctorDisruptions_DisruptionId] FOREIGN KEY ([DisruptionId]) REFERENCES [DoctorDisruptions] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124024843_AddDoctorDisruptions', N'10.0.0');

COMMIT;
GO

