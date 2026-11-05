/* =============================================================================
   Backfill: convert old UTC scrap approval dates to local time (SAST, UTC+2)

   Why: approvals were stamped with DateTime.UtcNow while scrap submission
   dates were local, so approval times looked ~2 hours earlier than they were
   (an item could appear approved before it was submitted). The app now uses
   local time for approvals. South Africa has no daylight saving, so +2 hours
   is exact.

   Columns shifted on scpScrapData (only where < @Cutoff):
     TeamLeaderApprovalDate, GroupLeaderApprovalDate, ManagerApprovalDate,
     SeniorManagerApprovalDate, ApprovalDate, FirstApprovalDate

   NOT shifted: ModifiedAt (mixed — local on submission, UTC on approval, so
   it can't be corrected reliably), SubmissionDate / CreatedAt (already local).

   IMPORTANT: set @Cutoff to the moment the new version was deployed.

   HOW TO RUN
     1. Set @Cutoff in BOTH steps, run step 1 and review.
     2. Run step 2 (backup + shift). It ends in ROLLBACK by default; change to
        COMMIT when the counts match step 1 and run again. Run COMMIT ONCE.
   ============================================================================= */

USE [IntegrationCompanyB];
GO

/* ── Step 1: PREVIEW ─────────────────────────────────────────────────────── */
DECLARE @Cutoff datetime = '2026-11-05 00:00';   -- ← set to the deployment time

SELECT
    SUM(CASE WHEN TeamLeaderApprovalDate    < @Cutoff THEN 1 ELSE 0 END) AS TeamLeaderDates,
    SUM(CASE WHEN GroupLeaderApprovalDate   < @Cutoff THEN 1 ELSE 0 END) AS GroupLeaderDates,
    SUM(CASE WHEN ManagerApprovalDate       < @Cutoff THEN 1 ELSE 0 END) AS ManagerDates,
    SUM(CASE WHEN SeniorManagerApprovalDate < @Cutoff THEN 1 ELSE 0 END) AS SeniorManagerDates,
    SUM(CASE WHEN ApprovalDate              < @Cutoff THEN 1 ELSE 0 END) AS FinalApprovalDates,
    SUM(CASE WHEN FirstApprovalDate         < @Cutoff THEN 1 ELSE 0 END) AS FirstApprovalDates,
    COUNT(*) AS RowsAffected
FROM dbo.scpScrapData
WHERE TeamLeaderApprovalDate < @Cutoff OR GroupLeaderApprovalDate < @Cutoff
   OR ManagerApprovalDate < @Cutoff OR SeniorManagerApprovalDate < @Cutoff
   OR ApprovalDate < @Cutoff OR FirstApprovalDate < @Cutoff;

-- Sanity check: items that currently look approved BEFORE they were submitted
-- (should mostly disappear after the backfill)
SELECT COUNT(*) AS ApprovedBeforeSubmitted_Now,
       SUM(CASE WHEN DATEADD(HOUR, 2, TeamLeaderApprovalDate) < SubmissionDate THEN 1 ELSE 0 END) AS ApprovedBeforeSubmitted_After
FROM dbo.scpScrapData
WHERE TeamLeaderApprovalDate < @Cutoff
  AND TeamLeaderApprovalDate < SubmissionDate;
GO

/* ── Step 2: BACKUP + SHIFT (defaults to ROLLBACK) ───────────────────────── */
DECLARE @Cutoff datetime = '2026-11-05 00:00';   -- ← same value as step 1

IF OBJECT_ID('dbo.scpApprovalDates_UtcBackfill_Backup', 'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.scpApprovalDates_UtcBackfill_Backup)
BEGIN
    RAISERROR('Backup table already has rows — the backfill appears to have been committed already. Stopping to avoid shifting twice.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('dbo.scpApprovalDates_UtcBackfill_Backup', 'U') IS NULL
    CREATE TABLE dbo.scpApprovalDates_UtcBackfill_Backup (
        ScrapId                   int      NOT NULL PRIMARY KEY,
        TeamLeaderApprovalDate    datetime NULL,
        GroupLeaderApprovalDate   datetime NULL,
        ManagerApprovalDate       datetime NULL,
        SeniorManagerApprovalDate datetime NULL,
        ApprovalDate              datetime NULL,
        FirstApprovalDate         datetime NULL,
        BackedUpAt                datetime NOT NULL DEFAULT GETDATE()
    );

SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT INTO dbo.scpApprovalDates_UtcBackfill_Backup
    (ScrapId, TeamLeaderApprovalDate, GroupLeaderApprovalDate, ManagerApprovalDate,
     SeniorManagerApprovalDate, ApprovalDate, FirstApprovalDate)
SELECT ScrapId, TeamLeaderApprovalDate, GroupLeaderApprovalDate, ManagerApprovalDate,
       SeniorManagerApprovalDate, ApprovalDate, FirstApprovalDate
FROM dbo.scpScrapData
WHERE TeamLeaderApprovalDate < @Cutoff OR GroupLeaderApprovalDate < @Cutoff
   OR ManagerApprovalDate < @Cutoff OR SeniorManagerApprovalDate < @Cutoff
   OR ApprovalDate < @Cutoff OR FirstApprovalDate < @Cutoff;

UPDATE dbo.scpScrapData
SET TeamLeaderApprovalDate    = CASE WHEN TeamLeaderApprovalDate    < @Cutoff THEN DATEADD(HOUR, 2, TeamLeaderApprovalDate)    ELSE TeamLeaderApprovalDate    END,
    GroupLeaderApprovalDate   = CASE WHEN GroupLeaderApprovalDate   < @Cutoff THEN DATEADD(HOUR, 2, GroupLeaderApprovalDate)   ELSE GroupLeaderApprovalDate   END,
    ManagerApprovalDate       = CASE WHEN ManagerApprovalDate       < @Cutoff THEN DATEADD(HOUR, 2, ManagerApprovalDate)       ELSE ManagerApprovalDate       END,
    SeniorManagerApprovalDate = CASE WHEN SeniorManagerApprovalDate < @Cutoff THEN DATEADD(HOUR, 2, SeniorManagerApprovalDate) ELSE SeniorManagerApprovalDate END,
    ApprovalDate              = CASE WHEN ApprovalDate              < @Cutoff THEN DATEADD(HOUR, 2, ApprovalDate)              ELSE ApprovalDate              END,
    FirstApprovalDate         = CASE WHEN FirstApprovalDate         < @Cutoff THEN DATEADD(HOUR, 2, FirstApprovalDate)         ELSE FirstApprovalDate         END
WHERE ScrapId IN (SELECT ScrapId FROM dbo.scpApprovalDates_UtcBackfill_Backup);

SELECT @@ROWCOUNT AS RowsShifted,
       (SELECT COUNT(*) FROM dbo.scpApprovalDates_UtcBackfill_Backup) AS RowsBackedUp;

ROLLBACK TRANSACTION;   -- ← change to COMMIT TRANSACTION once the counts match step 1
GO

/* ── Step 3: UNDO (only if a committed backfill must be reversed) ────────── */
/*
UPDATE s
SET s.TeamLeaderApprovalDate    = b.TeamLeaderApprovalDate,
    s.GroupLeaderApprovalDate   = b.GroupLeaderApprovalDate,
    s.ManagerApprovalDate       = b.ManagerApprovalDate,
    s.SeniorManagerApprovalDate = b.SeniorManagerApprovalDate,
    s.ApprovalDate              = b.ApprovalDate,
    s.FirstApprovalDate         = b.FirstApprovalDate
FROM dbo.scpScrapData s
JOIN dbo.scpApprovalDates_UtcBackfill_Backup b ON b.ScrapId = s.ScrapId;

DELETE FROM dbo.scpApprovalDates_UtcBackfill_Backup;
*/
