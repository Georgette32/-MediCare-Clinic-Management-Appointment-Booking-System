IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] uniqueidentifier NOT NULL,
    [FullName] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [Specializations] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Description] nvarchar(max) NULL,
    CONSTRAINT [PK_Specializations] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] uniqueidentifier NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] uniqueidentifier NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] uniqueidentifier NOT NULL,
    [RoleId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] uniqueidentifier NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Patients] (
    [Id] uniqueidentifier NOT NULL,
    [ApplicationUserId] uniqueidentifier NOT NULL,
    [FullName] nvarchar(200) NOT NULL,
    [PhoneNumber] nvarchar(32) NOT NULL,
    CONSTRAINT [PK_Patients] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Patients_AspNetUsers_ApplicationUserId] FOREIGN KEY ([ApplicationUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Doctors] (
    [Id] uniqueidentifier NOT NULL,
    [ApplicationUserId] uniqueidentifier NOT NULL,
    [FullName] nvarchar(200) NOT NULL,
    [SpecializationId] uniqueidentifier NOT NULL,
    [AppointmentDurationMinutes] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Doctors] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Doctors_AppointmentDurationPositive] CHECK ([AppointmentDurationMinutes] > 0),
    CONSTRAINT [FK_Doctors_AspNetUsers_ApplicationUserId] FOREIGN KEY ([ApplicationUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Doctors_Specializations_SpecializationId] FOREIGN KEY ([SpecializationId]) REFERENCES [Specializations] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Appointments] (
    [Id] uniqueidentifier NOT NULL,
    [PatientId] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [RescheduledFromAppointmentId] uniqueidentifier NULL,
    [AppointmentDate] date NOT NULL,
    [StartTime] time NOT NULL,
    [EndTime] time NOT NULL,
    [Status] nvarchar(24) NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NULL,
    [CancellationReason] nvarchar(500) NULL,
    CONSTRAINT [PK_Appointments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Appointments_TimeRange] CHECK ([EndTime] > [StartTime]),
    CONSTRAINT [FK_Appointments_Appointments_RescheduledFromAppointmentId] FOREIGN KEY ([RescheduledFromAppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Appointments_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Appointments_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [DoctorBreaks] (
    [Id] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [Date] date NOT NULL,
    [StartTime] time NOT NULL,
    [EndTime] time NOT NULL,
    CONSTRAINT [PK_DoctorBreaks] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DoctorBreaks_TimeRange] CHECK ([EndTime] > [StartTime]),
    CONSTRAINT [FK_DoctorBreaks_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [DoctorHolidays] (
    [Id] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NOT NULL,
    [Reason] nvarchar(500) NULL,
    CONSTRAINT [PK_DoctorHolidays] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DoctorHolidays_DateRange] CHECK ([EndDate] >= [StartDate]),
    CONSTRAINT [FK_DoctorHolidays_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [DoctorWorkingHours] (
    [Id] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [DayOfWeek] int NOT NULL,
    [StartTime] time NOT NULL,
    [EndTime] time NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_DoctorWorkingHours] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DoctorWorkingHours_TimeRange] CHECK ([EndTime] > [StartTime]),
    CONSTRAINT [FK_DoctorWorkingHours_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [QueueEntries] (
    [Id] uniqueidentifier NOT NULL,
    [PatientId] uniqueidentifier NOT NULL,
    [DoctorId] uniqueidentifier NOT NULL,
    [AppointmentId] uniqueidentifier NULL,
    [QueueDate] date NOT NULL,
    [Source] nvarchar(16) NOT NULL,
    [ArrivedAt] datetimeoffset NOT NULL,
    [QueueSequence] bigint NULL,
    [Status] nvarchar(24) NOT NULL,
    [CalledAt] datetimeoffset NULL,
    [ConsultationStartedAt] datetimeoffset NULL,
    [CompletedAt] datetimeoffset NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_QueueEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_QueueEntries_SourceAppointmentSequence] CHECK (([Source] = 'WalkIn' AND [AppointmentId] IS NULL AND [QueueSequence] IS NOT NULL AND [QueueSequence] > 0) OR ([Source] = 'Scheduled' AND [AppointmentId] IS NOT NULL AND [QueueSequence] IS NULL)),
    CONSTRAINT [FK_QueueEntries_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_QueueEntries_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_QueueEntries_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_Appointments_DoctorId_AppointmentDate_StartTime] ON [Appointments] ([DoctorId], [AppointmentDate], [StartTime]) WHERE [Status] IN ('Pending', 'Confirmed');

CREATE INDEX [IX_Appointments_DoctorId_AppointmentDate_StartTime_EndTime] ON [Appointments] ([DoctorId], [AppointmentDate], [StartTime], [EndTime]);

CREATE INDEX [IX_Appointments_PatientId_AppointmentDate] ON [Appointments] ([PatientId], [AppointmentDate]);

CREATE UNIQUE INDEX [IX_Appointments_RescheduledFromAppointmentId] ON [Appointments] ([RescheduledFromAppointmentId]) WHERE [RescheduledFromAppointmentId] IS NOT NULL AND [Status] IN ('Pending', 'Confirmed');

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_DoctorBreaks_DoctorId_Date_StartTime] ON [DoctorBreaks] ([DoctorId], [Date], [StartTime]);

CREATE INDEX [IX_DoctorHolidays_DoctorId_StartDate_EndDate] ON [DoctorHolidays] ([DoctorId], [StartDate], [EndDate]);

CREATE UNIQUE INDEX [IX_Doctors_ApplicationUserId] ON [Doctors] ([ApplicationUserId]);

CREATE INDEX [IX_Doctors_SpecializationId] ON [Doctors] ([SpecializationId]);

CREATE UNIQUE INDEX [IX_DoctorWorkingHours_DoctorId_DayOfWeek_StartTime] ON [DoctorWorkingHours] ([DoctorId], [DayOfWeek], [StartTime]);

CREATE UNIQUE INDEX [IX_Patients_ApplicationUserId] ON [Patients] ([ApplicationUserId]);

CREATE UNIQUE INDEX [IX_QueueEntries_AppointmentId] ON [QueueEntries] ([AppointmentId]) WHERE [AppointmentId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_QueueEntries_DoctorId] ON [QueueEntries] ([DoctorId]) WHERE [Status] = 'InConsultation';

CREATE UNIQUE INDEX [IX_QueueEntries_DoctorId_QueueDate_QueueSequence] ON [QueueEntries] ([DoctorId], [QueueDate], [QueueSequence]) WHERE [QueueSequence] IS NOT NULL;

CREATE INDEX [IX_QueueEntries_DoctorId_Source_QueueDate_ArrivedAt_QueueSequence] ON [QueueEntries] ([DoctorId], [Source], [QueueDate], [ArrivedAt], [QueueSequence]);

CREATE INDEX [IX_QueueEntries_PatientId] ON [QueueEntries] ([PatientId]);

CREATE UNIQUE INDEX [IX_Specializations_Name] ON [Specializations] ([Name]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261121190210_InitialCreate', N'11.0.0');

COMMIT;
GO

