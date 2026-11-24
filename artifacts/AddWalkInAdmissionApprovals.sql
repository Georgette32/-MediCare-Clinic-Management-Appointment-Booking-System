BEGIN TRANSACTION;
CREATE TABLE [WalkInAdmissionApprovals] (
    [Id] uniqueidentifier NOT NULL,
    [QueueEntryId] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [Decision] nvarchar(16) NOT NULL,
    [RequestedAtUtc] datetimeoffset NOT NULL,
    [ExpiresAtUtc] datetimeoffset NOT NULL,
    [DecisionAtUtc] datetimeoffset NULL,
    [UsedAtUtc] datetimeoffset NULL,
    [Notes] nvarchar(max) NULL,
    CONSTRAINT [PK_WalkInAdmissionApprovals] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_WalkInAdmissionApprovals_DecisionTimestamp] CHECK ([Decision] IN ('Pending', 'Approved', 'Rejected', 'Expired') AND (([Decision] = 'Pending' AND [DecisionAtUtc] IS NULL AND [UsedAtUtc] IS NULL) OR ([Decision] = 'Approved' AND [DecisionAtUtc] IS NOT NULL) OR ([Decision] IN ('Rejected', 'Expired') AND [DecisionAtUtc] IS NOT NULL AND [UsedAtUtc] IS NULL)) AND ([UsedAtUtc] IS NULL OR [DecisionAtUtc] IS NULL OR [UsedAtUtc] >= [DecisionAtUtc])),
    CONSTRAINT [FK_WalkInAdmissionApprovals_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WalkInAdmissionApprovals_QueueEntries_QueueEntryId] FOREIGN KEY ([QueueEntryId]) REFERENCES [QueueEntries] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_WalkInAdmissionApprovals_DoctorId_Decision_RequestedAtUtc] ON [WalkInAdmissionApprovals] ([DoctorId], [Decision], [RequestedAtUtc]);

CREATE UNIQUE INDEX [IX_WalkInAdmissionApprovals_QueueEntryId] ON [WalkInAdmissionApprovals] ([QueueEntryId]) WHERE [Decision] = 'Pending';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261123020059_AddWalkInAdmissionApprovals', N'10.0.0');

COMMIT;
GO
