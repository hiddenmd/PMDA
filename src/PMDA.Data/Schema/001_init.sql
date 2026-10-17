IF OBJECT_ID(N'dbo.Flight', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Flight
    (
        FlightId       uniqueidentifier NOT NULL CONSTRAINT PK_Flight PRIMARY KEY,
        CustomerId     nvarchar(32)     NOT NULL,
        FormatVersion  int              NOT NULL,
        Tail           nvarchar(32)     NOT NULL,
        MissionId      nvarchar(64)     NOT NULL,
        StartUtc       datetime2(7)     NOT NULL,
        FirstEventUtc  datetime2(7)     NULL,
        LastEventUtc   datetime2(7)     NULL,
        SourceName     nvarchar(400)    NOT NULL,
        FileSha256     binary(32)       NULL,
        Status         tinyint          NOT NULL, -- 0 Importing, 1 Completed, 2 Failed
        FailureReason  nvarchar(1000)   NULL,
        EventCount     bigint           NOT NULL CONSTRAINT DF_Flight_EventCount DEFAULT 0,
        ErrorCount     bigint           NOT NULL CONSTRAINT DF_Flight_ErrorCount DEFAULT 0,
        ImportedUtc    datetime2(7)     NOT NULL CONSTRAINT DF_Flight_ImportedUtc DEFAULT SYSUTCDATETIME()
    );

    -- Same file cannot be imported twice; failed imports may be retried.
    CREATE UNIQUE INDEX UX_Flight_FileSha256 ON dbo.Flight (FileSha256)
        WHERE FileSha256 IS NOT NULL AND Status <> 2;
END;

IF OBJECT_ID(N'dbo.FlightEvent', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FlightEvent
    (
        FlightId      uniqueidentifier NOT NULL,
        Seq           bigint           NOT NULL,
        TimeUtc       datetime2(7)     NOT NULL,
        EventType     tinyint          NOT NULL,
        SubType       smallint         NULL,
        EmitterId     int              NULL,
        FrequencyMHz  real             NULL,
        PriUs         real             NULL,
        PulseWidthUs  real             NULL,
        BearingDeg    real             NULL,
        AmplitudeDb   real             NULL,
        Lat           float            NULL,
        Lon           float            NULL,
        AltM          real             NULL,
        [Count]       int              NULL,
        Extra         varbinary(max)   NULL
    );

    -- Columnstore: high compression and fast scans over millions of events per flight.
    -- Rows arrive per flight in time order, so segments eliminate well on FlightId/TimeUtc.
    CREATE CLUSTERED COLUMNSTORE INDEX CCI_FlightEvent ON dbo.FlightEvent;
END;
