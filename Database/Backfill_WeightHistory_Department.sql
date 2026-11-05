/* =============================================================================
   Backfill: scpWeightHistory.Department from scpScrapData (by BatchScrapId)

   Why: before the fix, operator weights (Print & Record Weight) were saved with
   the operator's PROFILE department instead of the department the scrap was
   captured against. This corrects those rows using the batch's scrap entries.

   Scope (only rows that are safe to correct):
     - operator weighings only: IsBulkWeighing = 0 AND BatchScrapId IS NOT NULL
     - the batch's scrap rows belong to the same operator (ClockNumber = CreatedBy)
     - the batch has exactly ONE distinct department (ambiguous batches are
       listed in step 2 and left alone)
     - the department actually differs

   HOW TO RUN
     1. Run steps 1 + 2 on their own and review the output.
     2. Run step 3. It takes a backup, updates inside a transaction, and ends in
        ROLLBACK by default. When the counts look right, change ROLLBACK to
        COMMIT and run step 3 again.
     3. Step 4 restores from the backup if ever needed.
   ============================================================================= */

USE [IntegrationCompanyB];
GO

/* ── Step 1: PREVIEW rows that will change ───────────────────────────────── */
;WITH BatchDept AS (
    SELECT s.BatchScrapId,
           s.ClockNumber,
           MIN(s.Department) AS Department
    FROM dbo.scpScrapData s
    WHERE s.BatchScrapId > 0
    GROUP BY s.BatchScrapId, s.ClockNumber
    HAVING COUNT(DISTINCT s.Department) = 1
)
SELECT w.WeightId,
       w.BatchScrapId,
       w.CreatedBy,
       w.CreatedAt,
       w.Weight,
       w.Department AS CurrentDepartment,
       bd.Department AS CorrectDepartment,
       w.Line,
       w.Shift
FROM dbo.scpWeightHistory w
JOIN BatchDept bd
  ON bd.BatchScrapId = w.BatchScrapId
 AND bd.ClockNumber  = w.CreatedBy
WHERE w.IsBulkWeighing = 0
  AND w.BatchScrapId IS NOT NULL
  AND ISNULL(w.Department, '') <> bd.Department
ORDER BY w.CreatedAt;

/* ── Step 2: AMBIGUOUS batches (more than one department) — NOT updated ─── */
SELECT w.WeightId,
       w.BatchScrapId,
       w.CreatedBy,
       w.Department AS CurrentDepartment,
       STRING_AGG(d.Department, ', ') AS BatchDepartments
FROM dbo.scpWeightHistory w
CROSS APPLY (
    SELECT DISTINCT s.Department
    FROM dbo.scpScrapData s
    WHERE s.BatchScrapId = w.BatchScrapId
      AND s.ClockNumber  = w.CreatedBy
) d
WHERE w.IsBulkWeighing = 0
  AND w.BatchScrapId IS NOT NULL
GROUP BY w.WeightId, w.BatchScrapId, w.CreatedBy, w.Department
HAVING COUNT(*) > 1;
GO

/* ── Step 3: BACKUP + UPDATE (defaults to ROLLBACK) ──────────────────────── */
IF OBJECT_ID('dbo.scpWeightHistory_DeptBackfill_Backup', 'U') IS NULL
BEGIN
    SELECT WeightId, Department, ModifiedAt, ModifiedBy, GETDATE() AS BackedUpAt
    INTO dbo.scpWeightHistory_DeptBackfill_Backup
    FROM dbo.scpWeightHistory
    WHERE 1 = 0;
END;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

;WITH BatchDept AS (
    SELECT s.BatchScrapId,
           s.ClockNumber,
           MIN(s.Department) AS Department
    FROM dbo.scpScrapData s
    WHERE s.BatchScrapId > 0
    GROUP BY s.BatchScrapId, s.ClockNumber
    HAVING COUNT(DISTINCT s.Department) = 1
)
SELECT w.WeightId, bd.Department AS CorrectDepartment
INTO #ToFix
FROM dbo.scpWeightHistory w
JOIN BatchDept bd
  ON bd.BatchScrapId = w.BatchScrapId
 AND bd.ClockNumber  = w.CreatedBy
WHERE w.IsBulkWeighing = 0
  AND w.BatchScrapId IS NOT NULL
  AND ISNULL(w.Department, '') <> bd.Department;

INSERT INTO dbo.scpWeightHistory_DeptBackfill_Backup (WeightId, Department, ModifiedAt, ModifiedBy, BackedUpAt)
SELECT w.WeightId, w.Department, w.ModifiedAt, w.ModifiedBy, GETDATE()
FROM dbo.scpWeightHistory w
JOIN #ToFix f ON f.WeightId = w.WeightId;

UPDATE w
SET w.Department = f.CorrectDepartment,
    w.ModifiedAt = GETDATE(),
    w.ModifiedBy = 'DEPT-BACKFILL'
FROM dbo.scpWeightHistory w
JOIN #ToFix f ON f.WeightId = w.WeightId;

SELECT @@ROWCOUNT AS RowsUpdated, (SELECT COUNT(*) FROM #ToFix) AS RowsExpected;

DROP TABLE #ToFix;

ROLLBACK TRANSACTION;   -- ← change to COMMIT TRANSACTION once the counts look right
GO

/* ── Step 4: RESTORE (only if you need to undo a committed backfill) ─────── */
/*
UPDATE w
SET w.Department = b.Department,
    w.ModifiedAt = b.ModifiedAt,
    w.ModifiedBy = b.ModifiedBy
FROM dbo.scpWeightHistory w
JOIN dbo.scpWeightHistory_DeptBackfill_Backup b ON b.WeightId = w.WeightId;
*/
