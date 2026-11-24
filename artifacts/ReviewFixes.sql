BEGIN TRANSACTION;
ALTER TABLE [DisruptionWorklistItems] ADD [ResolutionNote] nvarchar(200) NULL;

ALTER TABLE [DisruptionWorklistItems] ADD [ResolvedAtUtc] datetimeoffset NULL;

UPDATE [DisruptionWorklistItems] SET [ResolvedAtUtc] = [UpdatedAtUtc] WHERE [Outcome] IN ('Rescheduled', 'Cancelled') AND [ResolvedAtUtc] IS NULL;

ALTER TABLE [DisruptionWorklistItems] ADD CONSTRAINT [CK_DisruptionWorklistItems_ClosedOutcomeResolved] CHECK ([Outcome] NOT IN ('Rescheduled', 'Cancelled') OR [ResolvedAtUtc] IS NOT NULL);

ALTER TABLE [DisruptionWorklistItems] ADD CONSTRAINT [CK_DisruptionWorklistItems_ResolutionNote] CHECK ([ResolvedAtUtc] IS NULL OR [Outcome] IN ('Rescheduled', 'Cancelled') OR [ResolutionNote] IS NOT NULL);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124034947_ReviewFixes', N'10.0.0');

COMMIT;
GO

