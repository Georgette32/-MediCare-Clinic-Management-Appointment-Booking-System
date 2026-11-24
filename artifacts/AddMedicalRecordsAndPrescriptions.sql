BEGIN TRANSACTION;
CREATE TABLE [MedicalRecords] (
    [Id] uniqueidentifier NOT NULL,
    [PatientId] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [QueueEntryId] uniqueidentifier NOT NULL,
    [AppointmentId] uniqueidentifier NULL,
    [VisitDate] date NOT NULL,
    [ChiefComplaint] nvarchar(1000) NULL,
    [Diagnosis] nvarchar(2000) NOT NULL,
    [ClinicalNotes] nvarchar(4000) NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NULL,
    CONSTRAINT [PK_MedicalRecords] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MedicalRecords_DiagnosisRequired] CHECK (LEN(LTRIM(RTRIM([Diagnosis]))) > 0),
    CONSTRAINT [CK_MedicalRecords_UpdatedAfterCreated] CHECK ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]),
    CONSTRAINT [FK_MedicalRecords_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MedicalRecords_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MedicalRecords_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MedicalRecords_QueueEntries_QueueEntryId] FOREIGN KEY ([QueueEntryId]) REFERENCES [QueueEntries] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [MedicalRecordAddenda] (
    [Id] uniqueidentifier NOT NULL,
    [MedicalRecordId] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [Text] nvarchar(2000) NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_MedicalRecordAddenda] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MedicalRecordAddenda_TextRequired] CHECK (LEN(LTRIM(RTRIM([Text]))) > 0),
    CONSTRAINT [FK_MedicalRecordAddenda_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MedicalRecordAddenda_MedicalRecords_MedicalRecordId] FOREIGN KEY ([MedicalRecordId]) REFERENCES [MedicalRecords] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Prescriptions] (
    [Id] uniqueidentifier NOT NULL,
    [MedicalRecordId] uniqueidentifier NOT NULL,
    [IssuedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NULL,
    [Notes] nvarchar(1000) NULL,
    CONSTRAINT [PK_Prescriptions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Prescriptions_NotesNotBlank] CHECK ([Notes] IS NULL OR LEN(LTRIM(RTRIM([Notes]))) > 0),
    CONSTRAINT [FK_Prescriptions_MedicalRecords_MedicalRecordId] FOREIGN KEY ([MedicalRecordId]) REFERENCES [MedicalRecords] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [PrescriptionItems] (
    [Id] uniqueidentifier NOT NULL,
    [PrescriptionId] uniqueidentifier NOT NULL,
    [MedicationName] nvarchar(200) NOT NULL,
    [Dosage] nvarchar(100) NOT NULL,
    [Frequency] nvarchar(100) NOT NULL,
    [DurationDays] int NOT NULL,
    [Instructions] nvarchar(500) NULL,
    CONSTRAINT [PK_PrescriptionItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PrescriptionItems_DurationPositive] CHECK ([DurationDays] > 0),
    CONSTRAINT [CK_PrescriptionItems_TextRequired] CHECK (LEN(LTRIM(RTRIM([MedicationName]))) > 0 AND LEN(LTRIM(RTRIM([Dosage]))) > 0 AND LEN(LTRIM(RTRIM([Frequency]))) > 0),
    CONSTRAINT [FK_PrescriptionItems_Prescriptions_PrescriptionId] FOREIGN KEY ([PrescriptionId]) REFERENCES [Prescriptions] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_MedicalRecordAddenda_DoctorId] ON [MedicalRecordAddenda] ([DoctorId]);

CREATE INDEX [IX_MedicalRecordAddenda_MedicalRecordId_CreatedAtUtc] ON [MedicalRecordAddenda] ([MedicalRecordId], [CreatedAtUtc]);

CREATE INDEX [IX_MedicalRecords_AppointmentId] ON [MedicalRecords] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL;

CREATE INDEX [IX_MedicalRecords_DoctorId_VisitDate] ON [MedicalRecords] ([DoctorId], [VisitDate]);

CREATE INDEX [IX_MedicalRecords_PatientId_VisitDate] ON [MedicalRecords] ([PatientId], [VisitDate]);

CREATE UNIQUE INDEX [IX_MedicalRecords_QueueEntryId] ON [MedicalRecords] ([QueueEntryId]);

CREATE INDEX [IX_PrescriptionItems_PrescriptionId] ON [PrescriptionItems] ([PrescriptionId]);

CREATE UNIQUE INDEX [IX_Prescriptions_MedicalRecordId] ON [Prescriptions] ([MedicalRecordId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124040738_AddMedicalRecordsAndPrescriptions', N'10.0.0');

COMMIT;
GO

