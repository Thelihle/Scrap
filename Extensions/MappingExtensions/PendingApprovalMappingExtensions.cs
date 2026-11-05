using SCP_Control.Models;
using SCP_Control.ViewModels;

namespace SCP_Control.Extensions.MappingExtensions
{
    public static class PendingApprovalMappingExtensions
    {
        public static PendingApprovalViewModel ToViewModel(this PendingScrapEntry entry)
        {
            if (entry == null)
                return null!;

            return new PendingApprovalViewModel
            {
                ScrapId = entry.ScrapId,
                ClockNumber = entry.ClockNumber,
                PartNumber = entry.PartNumber,
                Quantity = entry.Quantity,
                TerminalQuantity = entry.TerminalQuantity,
                WeightKg = entry.WeightKg,
                Length = entry.Length,
                Section = entry.SectionName,
                ScrapReason = entry.ScrapReason,
                SectionName = entry.SectionName,
                ReasonName = entry.ReasonName,
                SubmissionDate = entry.SubmissionDate,
                ScrapSection = entry.Section
            };
        }

        public static ICollection<PendingApprovalViewModel> ToViewModels(
            this IEnumerable<PendingScrapEntry> entries)
        {
            if (entries == null)
                return new List<PendingApprovalViewModel>();

            return entries.Select(e => e.ToViewModel()).ToList();
        }
    }
}