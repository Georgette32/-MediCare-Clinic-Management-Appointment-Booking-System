BEGIN TRANSACTION;
CREATE TABLE [DoctorWorkingHourExtensions] (
    [Id] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [Date] date NOT NULL,
    [OriginalEndTime] time NOT NULL,
    [ExtendedEndTime] time NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    [Status] nvarchar(16) NOT NULL,
    [RequestedByUserId] uniqueidentifier NOT NULL,
    [RequestedAtUtc] datetimeoffset NOT NULL,
    [DecidedAtUtc] datetimeoffset NULL,
    [DecisionNotes] nvarchar(500) NULL,
    CONSTRAINT [PK_DoctorWorkingHourExtensions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DoctorWorkingHourExtensions_DecidedAfterRequest] CHECK ([DecidedAtUtc] IS NULL OR [DecidedAtUtc] >= [RequestedAtUtc]),
    CONSTRAINT [CK_DoctorWorkingHourExtensions_DecisionTimestamp] CHECK (([Status] = 'Pending' AND [DecidedAtUtc] IS NULL) OR ([Status] IN ('Approved', 'Rejected', 'Cancelled') AND [DecidedAtUtc] IS NOT NULL)),
    CONSTRAINT [CK_DoctorWorkingHourExtensions_Status] CHECK ([Status] IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
    CONSTRAINT [CK_DoctorWorkingHourExtensions_TimeRange] CHECK ([ExtendedEndTime] > [OriginalEndTime]),
    CONSTRAINT [FK_DoctorWorkingHourExtensions_AspNetUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DoctorWorkingHourExtensions_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_DoctorWorkingHourExtensions_DoctorId_Date] ON [DoctorWorkingHourExtensions] ([DoctorId], [Date]);

CREATE UNIQUE INDEX [IX_DoctorWorkingHourExtensions_DoctorId_Date_OriginalEndTime] ON [DoctorWorkingHourExtensions] ([DoctorId], [Date], [OriginalEndTime]) WHERE [Status] IN ('Pending', 'Approved');

CREATE INDEX [IX_DoctorWorkingHourExtensions_RequestedByUserId] ON [DoctorWorkingHourExtensions] ([RequestedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124022220_AddDoctorWorkingHourExtensions', N'10.0.0');

COMMIT;
GO

