/* =============================================================================
   Check / resync seq_WeightHistoryId against scpWeightHistory.WeightId

   Why: some code paths inserted WeightId as MAX(WeightId)+1 instead of using
   this sequence. If those rows went past the sequence's current value, the
   sequence will hand out IDs that already exist and inserts fail with a
   primary-key violation (operator weights now fail loudly instead of silently).

   HOW TO RUN
     1. Run step 1. If Status = 'OK', stop — nothing to do.
     2. If Status = 'BEHIND', run step 2 (it restarts the sequence just above
        the highest existing WeightId). Safe to re-run.
   ============================================================================= */

USE [IntegrationCompanyB];
GO

/* ── Step 1: CHECK ───────────────────────────────────────────────────────── */
SELECT CAST(s.current_value AS bigint)            AS SequenceCurrentValue,
       (SELECT MAX(WeightId) FROM dbo.scpWeightHistory) AS MaxWeightId,
       CASE WHEN CAST(s.current_value AS bigint) >= ISNULL((SELECT MAX(WeightId) FROM dbo.scpWeightHistory), 0)
            THEN 'OK' ELSE 'BEHIND' END           AS Status
FROM sys.sequences s
WHERE s.name = 'seq_WeightHistoryId'
  AND SCHEMA_NAME(s.schema_id) = 'dbo';
GO

/* ── Step 2: RESYNC (only if step 1 says BEHIND) ─────────────────────────── */
/*
DECLARE @next bigint = ISNULL((SELECT MAX(WeightId) FROM dbo.scpWeightHistory), 0) + 1;
DECLARE @sql nvarchar(200) = N'ALTER SEQUENCE dbo.seq_WeightHistoryId RESTART WITH ' + CAST(@next AS nvarchar(20));
EXEC sp_executesql @sql;
SELECT @next AS SequenceRestartedAt;
*/
