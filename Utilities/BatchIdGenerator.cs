// Utilities/BatchIdGenerator.cs
namespace SCP_Control.Utilities
{
    public static class BatchIdGenerator
    {
        /// <summary>
        /// Generates a unique batch ID for scrap submissions
        /// Format: BATCH-{ClockNumber}-{Timestamp}-{RandomSuffix}
        /// Example: BATCH-EMP001-20260121092315-A3F2
        /// </summary>
        public static string GenerateBatchId(string clockNumber)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var randomSuffix = Guid.NewGuid().ToString("N")[..4].ToUpper(); // 4 random chars

            return $"BATCH-{clockNumber}-{timestamp}-{randomSuffix}";
        }

        /// <summary>
        /// Generates a shorter batch ID (alternative format)
        /// Format: B-{Date}-{Time}-{Random}
        /// Example: B-20260121-092315-A3F2
        /// </summary>
        public static string GenerateShortBatchId(string clockNumber)
        {
            var date = DateTime.Now.ToString("yyyyMMdd");
            var time = DateTime.Now.ToString("HHmmss");
            var randomSuffix = Guid.NewGuid().ToString("N")[..4].ToUpper();

            return $"B-{date}-{time}-{randomSuffix}";
        }

        /// <summary>
        /// Extracts information from a batch ID
        /// </summary>
        public static (string ClockNumber, DateTime Timestamp, string Suffix) ParseBatchId(string batchId)
        {
            try
            {
                var parts = batchId.Split('-');
                if (parts.Length >= 4 && parts[0] == "BATCH")
                {
                    var clockNumber = parts[1];
                    var timestampStr = parts[2];
                    var suffix = parts[3];

                    var timestamp = DateTime.ParseExact(timestampStr, "yyyyMMddHHmmss", null);

                    return (clockNumber, timestamp, suffix);
                }
            }
            catch
            {
                // Return defaults if parsing fails
            }

            return (string.Empty, DateTime.MinValue, string.Empty);
        }
    }
}