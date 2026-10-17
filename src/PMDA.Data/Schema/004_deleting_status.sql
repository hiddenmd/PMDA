-- Status 3 = Deleting: hidden at once, data purged in the background. A file whose flight is being
-- deleted may be imported again straight away, so the duplicate check ignores Failed and Deleting.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Flight_FileSha256' AND object_id = OBJECT_ID(N'dbo.Flight')
           AND filter_definition NOT LIKE N'%3%')
BEGIN
    DROP INDEX UX_Flight_FileSha256 ON dbo.Flight;
    CREATE UNIQUE INDEX UX_Flight_FileSha256 ON dbo.Flight (FileSha256)
        WHERE FileSha256 IS NOT NULL AND Status <> 2 AND Status <> 3;
END;
