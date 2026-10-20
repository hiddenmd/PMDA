-- Header-defined formats (src/PMDA.Formats): every raw record of a flight, the typed tables generated per
-- project, and the headers those tables were generated from, so a database describes itself.
IF OBJECT_ID(N'dbo.RawRecord', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RawRecord
    (
        FlightId   uniqueidentifier NOT NULL,
        RecordSeq  bigint           NOT NULL,
        TimeUtc    datetime2(7)     NOT NULL,
        EventId    int              NOT NULL,
        Bytes      varbinary(max)   NOT NULL,
        CONSTRAINT PK_RawRecord PRIMARY KEY CLUSTERED (FlightId, RecordSeq) WITH (DATA_COMPRESSION = PAGE)
    );
END;

IF OBJECT_ID(N'dbo.FormatTable', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormatTable
    (
        SchemaName  sysname       NOT NULL,
        TableName   sysname       NOT NULL,
        ProjectId   nvarchar(32)  NOT NULL,
        MessageKey  nvarchar(128) NOT NULL,
        ParentTable sysname       NULL, -- set on child tables of <SubStructure> arrays
        CONSTRAINT PK_FormatTable PRIMARY KEY (SchemaName, TableName)
    );
END;

IF OBJECT_ID(N'dbo.FormatVersion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormatVersion
    (
        ProjectId    nvarchar(32)   NOT NULL,
        Version      int            NOT NULL,
        Sha256       binary(32)     NOT NULL,
        HeaderText   nvarchar(max)  NOT NULL,
        ProjectJson  nvarchar(max)  NOT NULL,
        InstalledUtc datetime2(7)   NOT NULL CONSTRAINT DF_FormatVersion_InstalledUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FormatVersion PRIMARY KEY (ProjectId, Version)
    );
END;
