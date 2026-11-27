BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots'
)
BEGIN
    ALTER TABLE [QueueEntries] ADD [ClosedAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots'
)
BEGIN
    CREATE TABLE [DoctorWorkingHourSnapshots] (
        [Id] uniqueidentifier NOT NULL,
        [WorkingHourId] uniqueidentifier NOT NULL,
        [DoctorId] uniqueidentifier NOT NULL,
        [DayOfWeek] int NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [RecordedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_DoctorWorkingHourSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DoctorWorkingHourSnapshots_DoctorWorkingHours_WorkingHourId] FOREIGN KEY ([WorkingHourId]) REFERENCES [DoctorWorkingHours] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoctorWorkingHourSnapshots_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots'
)
BEGIN
    CREATE INDEX [IX_DoctorWorkingHourSnapshots_DoctorId_EffectiveFrom] ON [DoctorWorkingHourSnapshots] ([DoctorId], [EffectiveFrom]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots'
)
BEGIN
    CREATE INDEX [IX_DoctorWorkingHourSnapshots_WorkingHourId] ON [DoctorWorkingHourSnapshots] ([WorkingHourId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots', N'10.0.0');
END;

COMMIT;
GO

