BEGIN TRANSACTION;
DROP INDEX [EmailIndex] ON [AspNetUsers];

ALTER TABLE [AspNetUsers] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);

CREATE UNIQUE INDEX [IX_Patients_PhoneNumber] ON [Patients] ([PhoneNumber]);

CREATE UNIQUE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124015135_AddAccountProvisioningSecurity', N'10.0.0');

COMMIT;
GO

