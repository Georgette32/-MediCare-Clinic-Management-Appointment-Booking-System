BEGIN TRANSACTION;
CREATE INDEX [IX_QueueEntries_QueueDate_DoctorId] ON [QueueEntries] ([QueueDate], [DoctorId]) INCLUDE ([Status], [Source], [PatientId], [AppointmentId], [ArrivedAt], [CalledAt], [ConsultationStartedAt], [CompletedAt]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124105540_AddAnalyticsIndexes', N'10.0.0');

COMMIT;
GO

