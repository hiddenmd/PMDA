IF OBJECT_ID(N'dbo.ImportDiagnostic', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImportDiagnostic
    (
        FlightId    uniqueidentifier NOT NULL,
        FileOffset  bigint           NOT NULL,
        Message     nvarchar(1000)   NOT NULL
    );

    CREATE CLUSTERED INDEX IX_ImportDiagnostic_Flight ON dbo.ImportDiagnostic (FlightId, FileOffset);
END;
