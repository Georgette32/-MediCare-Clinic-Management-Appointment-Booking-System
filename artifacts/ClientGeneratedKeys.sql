BEGIN TRANSACTION;
ALTER TABLE [PrescriptionItems] DROP CONSTRAINT [FK_PrescriptionItems_Prescriptions_PrescriptionId];

ALTER TABLE [PrescriptionItems] ADD CONSTRAINT [FK_PrescriptionItems_Prescriptions_PrescriptionId] FOREIGN KEY ([PrescriptionId]) REFERENCES [Prescriptions] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124112210_ClientGeneratedKeys', N'10.0.0');

COMMIT;
GO

