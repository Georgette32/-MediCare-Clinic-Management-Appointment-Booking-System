BEGIN TRANSACTION;
DROP INDEX [IX_QueueEntries_AppointmentId] ON [QueueEntries];

CREATE UNIQUE INDEX [IX_QueueEntries_AppointmentId] ON [QueueEntries] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL AND [Status] IN ('Waiting', 'Called', 'InConsultation');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124020835_NarrowQueueAppointmentIndexToLiveAttempts', N'10.0.0');

COMMIT;
GO

