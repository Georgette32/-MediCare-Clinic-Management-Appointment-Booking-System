BEGIN TRANSACTION;
ALTER TABLE [Doctors] ADD [Biography] nvarchar(1000) NULL;

CREATE TABLE [ClinicSettings] (
    [Id] int NOT NULL,
    [MinimumConsultationSampleSize] int NULL,
    [UpdatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_ClinicSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ClinicSettings_MinimumSampleSizePositive] CHECK ([MinimumConsultationSampleSize] IS NULL OR [MinimumConsultationSampleSize] > 0),
    CONSTRAINT [CK_ClinicSettings_SingletonId] CHECK ([Id] = 1),
    CONSTRAINT [FK_ClinicSettings_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_ClinicSettings_UpdatedByUserId] ON [ClinicSettings] ([UpdatedByUserId]);

EXEC sp_executesql N'CREATE TRIGGER [TR_DoctorWorkingHours_NoActiveOverlap]
ON [DoctorWorkingHours]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM [inserted] AS [i]
        INNER JOIN [DoctorWorkingHours] AS [o]
            ON [o].[DoctorId] = [i].[DoctorId]
           AND [o].[DayOfWeek] = [i].[DayOfWeek]
           AND [o].[Id] <> [i].[Id]
        WHERE [i].[IsActive] = 1
          AND [o].[IsActive] = 1
          AND [o].[StartTime] < [i].[EndTime]
          AND [o].[EndTime] > [i].[StartTime])
    BEGIN
        THROW 50001, ''Overlapping active working-hour windows are not allowed for the same doctor and weekday.'', 1;
    END
END';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261124030608_AddScheduleManagementAndClinicSettings', N'10.0.0');

COMMIT;
GO

