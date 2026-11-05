/* =============================================================================
   Backfill: convert old UTC timestamps on weight / bin-sale data to local time

   Why: admin weight saves (bulk bin, bulk location, SaveWeight, monthly,
   daily total) and bin sales used GETUTCDATE(); operator weighings always used
   local time. The app now writes local time everywhere for this data, so the
   old UTC rows are 2 hours behind. South Africa (SAST, UTC+2) has no daylight
   saving, so +2 hours is exact.

   Which rows were UTC:
     - scpWeightHistory rows WITHOUT a BatchScrapId (operator rows have one and
       were always local)
     - scpBinSale.CreatedAt (SaleDate is a plain date and is NOT changed)
     - scpDailyReportNote CreatedAt / ModifiedAt

   IMPORTANT: set @Cutoff to the moment the new version was deployed. Rows
   created after that are already local and must not be shifted.

   HOW TO RUN
     1. Set @Cutoff in BOTH steps, run step 1 and review the counts.
     2. Run step 2. It backs up the old values and ends in ROLLBACK by default.
        Change ROLLBACK to COMMIT when the counts match step 1, and run again.
     3. Run step 2 only ONCE with COMMIT — running it twice adds 4 hours.
   ============================================================================= */

USE [IntegrationCompanyB];
GO

/* ── Step 1: PREVIEW ─────────────────────────────────────────────────────── */
DECLARE @Cutoff datetime = '2026-11-05 00:00';   -- ← set to the deployment time

SELECT 'scpWeightHistory' AS TableName,
       COUNT(*) AS RowsToShift,
       MIN(CreatedAt) AS OldestCreatedAt, MAX(CreatedAt) AS NewestCreatedAt
FROM dbo.scpWeightHistory
WHERE BatchScrapId IS NULL AND CreatedAt < @Cutoff
UNION ALL
SELECT 'scpBinSale', COUNT(*), MIN(CreatedAt), MAX(CreatedAt)
FROM dbo.scpBinSale
WHERE CreatedAt < @Cutoff
UNION ALL
SELECT 'scpDailyReportNote', COUNT(*), MIN(CreatedAt), MAX(CreatedAt)
FROM dbo.scpDailyReportNote
WHERE CreatedAt < @Cutoff;

-- Breakdown of weight rows by type, so you can see what will move
SELECT Shift, IsBulkWeighing, COUNT(*) AS RowsToShift
FROM dbo.scpWeightHistory
WHERE BatchScrapId IS NULL AND CreatedAt < @Cutoff
GROUP BY Shift, IsBulkWeighing
ORDER BY COUNT(*) DESC;
GO

/* ── Step 2: BACKUP + SHIFT (defaults to ROLLBACK) ───────────────────────── */
DECLARE @Cutoff datetime = '2026-11-05 00:00';   -- ← same value as step 1

IF OBJECT_ID('dbo.scpWeightTimes_UtcBackfill_Backup', 'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.scpWeightTimes_UtcBackfill_Backup)
BEGIN
    RAISERROR('Backup table already has rows — the backfill appears to have been committed already. Stopping to avoid shifting twice.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('dbo.scpWeightTimes_UtcBackfill_Backup', 'U') IS NULL
    CREATE TABLE dbo.scpWeightTimes_UtcBackfill_Backup (
        TableName   sysname   NOT NULL,
        RowKey      nvarchar(100) NOT NULL,
        CreatedAt   datetime  NULL,
        ModifiedAt  datetime  NULL,
        BackedUpAt  datetime  NOT NULL DEFAULT GETDATE()
    );

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- scpWeightHistory
INSERT INTO dbo.scpWeightTimes_UtcBackfill_Backup (TableName, RowKey, CreatedAt, ModifiedAt)
SELECT 'scpWeightHistory', CAST(WeightId AS nvarchar(100)), CreatedAt, ModifiedAt
FROM dbo.scpWeightHistory
WHERE BatchScrapId IS NULL AND CreatedAt < @Cutoff;

UPDATE dbo.scpWeightHistory
SET CreatedAt  = DATEADD(HOUR, 2, CreatedAt),
    ModifiedAt = CASE WHEN ModifiedAt < @Cutoff THEN DATEADD(HOUR, 2, ModifiedAt) ELSE ModifiedAt END
WHERE BatchScrapId IS NULL AND CreatedAt < @Cutoff;
SELECT 'scpWeightHistory' AS TableName, @@ROWCOUNT AS RowsShifted;

-- scpBinSale (CreatedAt only; SaleDate is a date chosen by the user)
INSERT INTO dbo.scpWeightTimes_UtcBackfill_Backup (TableName, RowKey, CreatedAt, ModifiedAt)
SELECT 'scpBinSale',
       CONCAT(Plant, '|', CategoryName, '|', CONVERT(varchar(23), CreatedAt, 121), '|', CreatedBy),
       CreatedAt, NULL
FROM dbo.scpBinSale
WHERE CreatedAt < @Cutoff;

UPDATE dbo.scpBinSale
SET CreatedAt = DATEADD(HOUR, 2, CreatedAt)
WHERE CreatedAt < @Cutoff;
SELECT 'scpBinSale' AS TableName, @@ROWCOUNT AS RowsShifted;

-- scpDailyReportNote
INSERT INTO dbo.scpWeightTimes_UtcBackfill_Backup (TableName, RowKey, CreatedAt, ModifiedAt)
SELECT 'scpDailyReportNote', CAST(NoteId AS nvarchar(100)), CreatedAt, ModifiedAt
FROM dbo.scpDailyReportNote
WHERE CreatedAt < @Cutoff;

UPDATE dbo.scpDailyReportNote
SET CreatedAt  = DATEADD(HOUR, 2, CreatedAt),
    ModifiedAt = CASE WHEN ModifiedAt < @Cutoff THEN DATEADD(HOUR, 2, ModifiedAt) ELSE ModifiedAt END
WHERE CreatedAt < @Cutoff;
SELECT 'scpDailyReportNote' AS TableName, @@ROWCOUNT AS RowsShifted;

ROLLBACK TRANSACTION;   -- ← change to COMMIT TRANSACTION once the counts match step 1
GO

/* ── Step 3: UNDO (only if a committed backfill must be reversed) ────────── */
/*
UPDATE w SET w.CreatedAt = b.CreatedAt, w.ModifiedAt = b.ModifiedAt
FROM dbo.scpWeightHistory w
JOIN dbo.scpWeightTimes_UtcBackfill_Backup b
  ON b.TableName = 'scpWeightHistory' AND b.RowKey = CAST(w.WeightId AS nvarchar(100));

UPDATE n SET n.CreatedAt = b.CreatedAt, n.ModifiedAt = b.ModifiedAt
FROM dbo.scpDailyReportNote n
JOIN dbo.scpWeightTimes_UtcBackfill_Backup b
  ON b.TableName = 'scpDailyReportNote' AND b.RowKey = CAST(n.NoteId AS nvarchar(100));

-- scpBinSale has no confirmed ID column: match on the key captured in the backup
UPDATE s SET s.CreatedAt = b.CreatedAt
FROM dbo.scpBinSale s
JOIN dbo.scpWeightTimes_UtcBackfill_Backup b
  ON b.TableName = 'scpBinSale'
 AND b.RowKey = CONCAT(s.Plant, '|', s.CategoryName, '|',
                       CONVERT(varchar(23), DATEADD(HOUR, -2, s.CreatedAt), 121), '|', s.CreatedBy);

DELETE FROM dbo.scpWeightTimes_UtcBackfill_Backup;
*/
