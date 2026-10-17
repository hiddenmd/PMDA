IF OBJECT_ID(N'dbo.AppUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUser
    (
        UserId           uniqueidentifier NOT NULL CONSTRAINT PK_AppUser PRIMARY KEY,
        UserName         nvarchar(64)     NOT NULL,
        DisplayName      nvarchar(128)    NOT NULL,
        PasswordHash     nvarchar(400)    NOT NULL,
        Role             tinyint          NOT NULL, -- 0 Analyst, 1 Admin
        IsActive         bit              NOT NULL CONSTRAINT DF_AppUser_IsActive DEFAULT 1,
        FailedAttempts   int              NOT NULL CONSTRAINT DF_AppUser_Failed DEFAULT 0,
        LockoutUntilUtc  datetime2(7)     NULL,
        -- Changes on password reset, role change or deactivation: invalidates existing sessions.
        SecurityStamp    uniqueidentifier NOT NULL,
        CreatedUtc       datetime2(7)     NOT NULL CONSTRAINT DF_AppUser_Created DEFAULT SYSUTCDATETIME(),
        LastLoginUtc     datetime2(7)     NULL
    );

    -- User names are unique regardless of case (the database collation is case-insensitive).
    CREATE UNIQUE INDEX UX_AppUser_UserName ON dbo.AppUser (UserName);
END;
