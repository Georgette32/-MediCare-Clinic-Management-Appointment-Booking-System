BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128005609_AddDoctorScheduledDurationChange'
)
BEGIN
    ALTER TABLE [Doctors] ADD [NextAppointmentDurationMinutes] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128005609_AddDoctorScheduledDurationChange'
)
BEGIN
    ALTER TABLE [Doctors] ADD [NextDurationEffectiveFrom] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128005609_AddDoctorScheduledDurationChange'
)
BEGIN
    EXEC(N'ALTER TABLE [Doctors] ADD CONSTRAINT [CK_Doctors_NextDuration] CHECK (([NextAppointmentDurationMinutes] IS NULL AND [NextDurationEffectiveFrom] IS NULL) OR ([NextAppointmentDurationMinutes] IS NOT NULL AND [NextDurationEffectiveFrom] IS NOT NULL AND [NextAppointmentDurationMinutes] > 0))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261128005609_AddDoctorScheduledDurationChange'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261128005609_AddDoctorScheduledDurationChange', N'10.0.0');
END;

COMMIT;
GO

