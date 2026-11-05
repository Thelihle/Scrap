// ============================================================
// ExportController.cs
// PDF = styled HTML page returned with print-ready CSS
// Excel = ClosedXML (already installed)
// ZERO new NuGet packages required.
// ============================================================

using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Text;

namespace SCP_Control.Controllers
{
    [Authorize(Policy = "ManagementOnly")]
    public class ExportController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExportController> _logger;
        private readonly string _connectionString;

        public ExportController(IConfiguration configuration, ILogger<ExportController> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection not found");
        }

        // ══════════════════════════════════════════════════════════════════
        //  EXCEL EXPORT
        // ══════════════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportToExcel(
            DateTime? startDate, DateTime? endDate,
            string department = "All Departments",
            string line = "All Lines",
            string shift = "All Shifts",
            bool exportAllData = false)
        {
            try
            {
                var (start, end) = ResolveDates(startDate, endDate);
                var data = (await FetchScrapData(start, end, department, line, shift)).ToList();

                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("Scrap Report");

                // ── Summary tab ──────────────────────────────────────────
                var infoRows = new[] {
                    ("Department", department), ("Line", line), ("Shift", shift),
                    ("Date Range", $"{start:dd MMM yyyy} – {end.AddDays(-1):dd MMM yyyy}"),
                    ("Generated",  DateTime.Now.ToString("dd MMM yyyy HH:mm")),
                    ("Total Items", data.Count.ToString())
                };

                int r = 1;
                foreach (var (lbl, val) in infoRows)
                {
                    ws.Cell(r, 1).Value = lbl;
                    ws.Cell(r, 1).Style.Font.Bold = true;
                    ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#428bca");
                    ws.Cell(r, 1).Style.Font.FontColor = XLColor.White;
                    ws.Cell(r, 2).Value = val;
                    r++;
                }

                r += 2; // gap

                // ── Header row ───────────────────────────────────────────
                var headers = new[] {
                    "ScrapId","Part Number","Description","Scrap Reason","Qty","UOM",
                    "Material Cost","Total Cost","Department","Line","Shift",
                    "Status","Submitted By","Submission Date",
                    "TL Approval","GL Approval","Manager Approval","Reject Reason"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(r, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Font.Bold = true;
                }
                r++;

                // ── Data rows ────────────────────────────────────────────
                bool alt = false;
                foreach (var d in data)
                {
                    var bg = alt ? XLColor.FromHtml("#f8f9fa") : XLColor.White;
                    string status = d.Status?.ToString() ?? "";
                    var statusColor = status == "Approved" ? XLColor.FromHtml("#28a745")
                        : status == "Rejected" ? XLColor.FromHtml("#dc3545")
                        : XLColor.FromHtml("#ffc107");

                    ws.Cell(r, 1).Value = (int)(d.ScrapId ?? 0);
                    ws.Cell(r, 2).Value = d.PartNumber?.ToString() ?? "";
                    ws.Cell(r, 3).Value = d.Description?.ToString() ?? "";
                    ws.Cell(r, 4).Value = d.ErrorName?.ToString() ?? "";   // ✅ NEW — Scrap Reason
                    ws.Cell(r, 5).Value = (double)(d.Quantity ?? 0);
                    ws.Cell(r, 6).Value = d.StockUom?.ToString() ?? "";
                    ws.Cell(r, 7).Value = (double)(d.MaterialCost ?? 0);
                    ws.Cell(r, 8).Value = (double)(d.TotalCost ?? 0);
                    ws.Cell(r, 9).Value = d.Department?.ToString() ?? "";
                    ws.Cell(r, 10).Value = d.Line?.ToString() ?? "";
                    ws.Cell(r, 11).Value = d.Shift?.ToString() ?? "";
                    ws.Cell(r, 12).Value = status;
                    ws.Cell(r, 13).Value = d.SubmittedBy?.ToString() ?? "";
                    ws.Cell(r, 14).Value = d.SubmissionDate != null
                        ? ((DateTime?)d.SubmissionDate).Value.ToString("yyyy-MM-dd HH:mm") : "";
                    ws.Cell(r, 15).Value = d.TeamLeaderApprovalStatus?.ToString() ?? "";
                    ws.Cell(r, 16).Value = d.GroupLeaderApprovalStatus?.ToString() ?? "";
                    ws.Cell(r, 17).Value = d.ManagerApprovalStatus?.ToString() ?? "";
                    ws.Cell(r, 18).Value = d.RejectedReason?.ToString() ?? "";

                    // Status colour — now column 12, not 11
                    ws.Cell(r, 12).Style.Font.FontColor = statusColor;
                    ws.Cell(r, 12).Style.Font.Bold = true;

                    for (int col = 1; col <= 18; col++)   // was 17
                        ws.Cell(r, col).Style.Fill.BackgroundColor = bg;

                    r++;
                    alt = !alt;
                }

                ws.Columns().AdjustToContents();

                // ── Format currency columns ──────────────────────────────
ws.Column(7).Style.NumberFormat.Format = "R #,##0.00";   // Material Cost — was Column(6)
ws.Column(8).Style.NumberFormat.Format = "R #,##0.00";   // Total Cost — was Column(7)

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                var fileName = $"ScrapReport_{Safe(department)}_{start:yyyyMMdd}_{end.AddDays(-1):yyyyMMdd}.xlsx";
                return File(stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excel export failed");
                TempData["ErrorMessage"] = "Excel export failed: " + ex.Message;
                return RedirectToAction("Index", "Analytics");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PDF EXPORT — returns styled HTML (print-ready)
        // ══════════════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportToPdf(
            DateTime? startDate, DateTime? endDate,
            string department = "All Departments",
            string line = "All Lines",
            string shift = "All Shifts",
            bool exportAllData = false)
        {
            try
            {
                var (start, end) = ResolveDates(startDate, endDate);

                // Fetch all data concurrently
                var scrapTask = FetchScrapData(start, end, department, line, shift);
                var statsTask = FetchStats(start, end, department, line, shift);
                var topTask = FetchTopParts(start, end, department, line, shift);
                var byLineTask = FetchByLine(start, end, department, shift);
                var byShiftTask = FetchByShift(start, end, department, line);
                var byReasonTask = FetchByReason(start, end, department, line, shift);
                var weightTask = FetchWeightSummary(start, end, department, line, shift);
                var approvalTask = FetchApprovalSummary(start, end, department, line, shift);

                await Task.WhenAll(scrapTask, statsTask, topTask, byLineTask,
                                   byShiftTask, byReasonTask, weightTask, approvalTask);

                var scrapData = (await scrapTask).ToList();
                var stats = await statsTask;
                var topParts = (await topTask).ToList();
                var byLine = (await byLineTask).ToList();
                var byShift = (await byShiftTask).ToList();
                var byReason = (await byReasonTask).ToList();
                var wt = await weightTask;
                var approvalData = (await approvalTask).ToList();

                // Computed values
                int total = (int)(stats?.TotalEntries ?? 0);
                int approved = (int)(stats?.ApprovedCount ?? 0);
                int rejected = (int)(stats?.RejectedCount ?? 0);
                int pending = (int)(stats?.PendingCount ?? 0);
                double cost = (double)(stats?.TotalCost ?? 0);
                double qty = (double)(stats?.TotalQuantity ?? 0);
                double totalWt = (double)(wt?.TotalWeight ?? 0);
                int weighings = (int)(wt?.TotalWeighings ?? 0);
                double approvalRate = total > 0 ? Math.Round(approved * 100.0 / total, 1) : 0;
                double rejectionRate = total > 0 ? Math.Round(rejected * 100.0 / total, 1) : 0;

                var html = BuildPdfHtml(
                    department, line, shift, start, end,
                    total, approved, rejected, pending,
                    cost, qty, totalWt, weighings,
                    approvalRate, rejectionRate,
                    topParts, byReason, byLine, byShift, approvalData, scrapData);

                // Return as HTML that auto-triggers browser print dialog
                return Content(html, "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF export failed");
                TempData["ErrorMessage"] = "PDF generation failed: " + ex.Message;
                return RedirectToAction("Index", "Analytics");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  HTML REPORT BUILDER
        // ══════════════════════════════════════════════════════════════════
        private string BuildPdfHtml(
            string dept, string line, string shift, DateTime start, DateTime end,
            int total, int approved, int rejected, int pending,
            double cost, double qty, double totalWt, int weighings,
            double approvalRate, double rejectionRate,
            List<dynamic> topParts, List<dynamic> byReason, List<dynamic> byLine,
            List<dynamic> byShift, List<dynamic> approvalData, List<dynamic> scrapData)
        {
            var sb = new StringBuilder();

            sb.Append(@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<title>Scrap Intelligence Report</title>
<style>
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: Arial, Helvetica, sans-serif; font-size: 9pt;
         color: #1a1a2e; background: #fff; }
  @media print {
    .no-print { display: none !important; }
    .page-break { page-break-before: always; }
    body { print-color-adjust: exact; -webkit-print-color-adjust: exact; }
  }

  /* ── PRINT BUTTON ── */
  .print-bar { background: #1a1a2e; padding: 12px 24px; position: sticky;
               top: 0; z-index: 999; display: flex; gap: 12px; align-items: center; }
  .btn-print  { background: #428bca; color: #fff; border: none; padding: 10px 24px;
               font-size: 14px; font-weight: 700; cursor: pointer; border-radius: 4px;
               letter-spacing: 1px; }
  .btn-print:hover { background: #3071a9; }
  .btn-close  { background: #dc3545; color: #fff; border: none; padding: 10px 20px;
               font-size: 14px; font-weight: 700; cursor: pointer; border-radius: 4px; }
  .print-hint { color: #5daeed; font-size: 12px; }

  /* ── LAYOUT ── */
  .report { max-width: 960px; margin: 0 auto; padding: 0 24px 40px; }

  /* ── COVER ── */
  .cover-header { background: #0f3460; color: #fff; padding: 32px 36px;
                  margin: 0 -24px 0; }
  .cover-accent { background: #428bca; height: 6px; margin: 0 -24px; }
  .company-name { color: #5daeed; font-size: 11pt; font-weight: 700;
                  letter-spacing: 3px; margin-bottom: 6px; }
  .report-title { font-size: 24pt; font-weight: 700; letter-spacing: 1px;
                  margin-bottom: 4px; }
  .report-sub   { color: #5daeed; font-size: 13pt; }

  /* ── INFO GRID ── */
  .info-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1px;
               background: #dee2e6; margin: 24px 0; border: 1px solid #dee2e6; }
  .info-cell { background: #f8f9fa; padding: 12px 16px; }
  .info-label { font-size: 8pt; color: #6c757d; font-weight: 700;
                text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
  .info-value { font-size: 13pt; font-weight: 700; color: #1a1a2e; }

  /* ── STAT BOXES ── */
  .stat-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0; margin-bottom: 24px; }
  .stat-box { padding: 16px; text-align: center; color: #fff; }
  .stat-num  { font-size: 28pt; font-weight: 700; line-height: 1; }
  .stat-lbl  { font-size: 8pt; margin-top: 4px; opacity: 0.9; font-weight: 700;
               letter-spacing: 1px; }
  .bg-primary { background: #428bca; }
  .bg-success { background: #28a745; }
  .bg-danger  { background: #dc3545; }
  .bg-warning { background: #ffc107; }

  /* ── SECTIONS ── */
  .section-header { display: flex; align-items: stretch; margin: 28px 0 16px; }
  .section-bar    { width: 6px; background: #428bca; flex-shrink: 0; }
  .section-title  { background: #0f3460; color: #fff; padding: 10px 18px;
                    font-size: 13pt; font-weight: 700; letter-spacing: 0.5px; flex: 1; }

  /* ── KPI GRID ── */
  .kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr);
              gap: 8px; margin-bottom: 20px; }
  .kpi-card { border: 1.5px solid; border-left-width: 5px;
              background: #f8f9fa; padding: 10px 12px; }
  .kpi-val  { font-size: 18pt; font-weight: 700; line-height: 1.1; }
  .kpi-name { font-size: 7.5pt; font-weight: 700; color: #1a1a2e; margin: 3px 0 1px; }
  .kpi-sub  { font-size: 7pt; color: #6c757d; }
  .c-primary { color: #428bca; border-color: #428bca; }
  .c-success { color: #28a745; border-color: #28a745; }
  .c-danger  { color: #dc3545; border-color: #dc3545; }
  .c-warning { color: #ffc107; border-color: #ffc107; }

  /* ── TABLES ── */
  table { width: 100%; border-collapse: collapse; margin-bottom: 8px; font-size: 8.5pt; }
  thead th { background: #1a1a2e; color: #fff; padding: 7px 8px;
             text-align: left; font-size: 8pt; font-weight: 700; }
  thead th.r { text-align: right; }
  tbody tr:nth-child(even) { background: #f8f9fa; }
  tbody tr:hover { background: #e3f2fd; }
  td { padding: 5px 8px; border-bottom: 1px solid #dee2e6; vertical-align: middle; }
  td.r { text-align: right; }
  td.bold { font-weight: 700; }
  td.top3 { color: #dc3545; font-weight: 700; }
  td.approved { color: #28a745; font-weight: 700; }
  td.rejected { color: #dc3545; font-weight: 700; }
  td.pending  { color: #ffc107; font-weight: 700; }

  /* ── INSIGHTS ── */
  .insights { background: #f0f7ff; border-left: 4px solid #428bca;
              padding: 14px 18px; margin: 16px 0; }
  .insights h4 { font-size: 10pt; color: #1a1a2e; margin-bottom: 8px;
                 font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; }
  .insight-item { font-size: 8.5pt; color: #1a1a2e; padding: 3px 0; line-height: 1.5; }

  /* ── FOOTER ── */
  .report-footer { background: #0f3460; color: #5daeed; padding: 12px 24px;
                   margin: 32px -24px -40px; font-size: 8pt;
                   display: flex; justify-content: space-between; }

  /* ── DETAIL TABLE ── */
  .detail-table th, .detail-table td { font-size: 7.5pt; padding: 4px 5px; }

  /* ── DIVIDER ── */
  hr.divider { border: none; border-top: 1px solid #dee2e6; margin: 20px 0; }
</style>
</head>
<body>");

            // ── Print bar ────────────────────────────────────────────────
            sb.Append($@"
<div class='print-bar no-print'>
  <button class='btn-print' onclick='window.print()'>🖨️  PRINT / SAVE AS PDF</button>
  <button class='btn-close' onclick=""window.location.href='/Analytics/Index'"">✕ Close</button>
  <span class='print-hint'>
    To save as PDF: Print → Destination → Save as PDF → Save
  </span>
</div>
<div class='report'>");

            // ════ COVER ══════════════════════════════════════════════════
            sb.Append($@"
<div class='cover-header'>
  <div class='company-name'>HESTO HARNESSES</div>
  <div class='report-title'>SCRAP MANAGEMENT SYSTEM</div>
  <div class='report-sub'>INTELLIGENCE REPORT</div>
</div>
<div class='cover-accent'></div>

<div class='info-grid'>
  {InfoCell("DEPARTMENT", dept == "All Departments" ? "All Departments" : dept)}
  {InfoCell("LINE / AREA", line == "All Lines" ? "All Lines" : line)}
  {InfoCell("SHIFT", shift == "All Shifts" ? "All Shifts" : shift)}
  {InfoCell("DATE RANGE", $"{start:dd MMM yyyy} – {end.AddDays(-1):dd MMM yyyy}")}
  {InfoCell("REPORT GENERATED", DateTime.Now.ToString("dd MMM yyyy HH:mm"))}
  {InfoCell("TOTAL RECORDS", total.ToString("N0"))}
</div>

<div class='stat-row'>
  {StatBox("TOTAL ITEMS", total.ToString("N0"), "bg-primary")}
  {StatBox("APPROVED", approved.ToString("N0"), "bg-success")}
  {StatBox("REJECTED", rejected.ToString("N0"), "bg-danger")}
  {StatBox("PENDING", pending.ToString("N0"), "bg-warning")}
</div>");

            // Insights
            sb.Append("<div class='insights'><h4>Key Insights</h4>");
            if (total > 0)
            {
                sb.Append($"<div class='insight-item'>• Approval rate of <strong>{approvalRate}%</strong> — {approved} of {total} items fully approved.</div>");
                sb.Append($"<div class='insight-item'>• Rejection rate of <strong>{rejectionRate}%</strong> — {rejected} items rejected in workflow.</div>");
                if (pending > 0) sb.Append($"<div class='insight-item'>• <strong>{pending}</strong> items still awaiting approval action.</div>");
                if (totalWt > 0) sb.Append($"<div class='insight-item'>• Total recorded scrap weight: <strong>{totalWt:N2} kg</strong> across {weighings} weighing record(s).</div>");
                if (cost > 0) sb.Append($"<div class='insight-item'>• Estimated total cost impact: <strong>R {cost:N2}</strong></div>");
                if (topParts.Any())
                {
                    string topPart = topParts.First().PartNumber?.ToString() ?? "N/A";
                    double topQty = (double)(topParts.First().TotalQuantity ?? 0);
                    sb.Append($"<div class='insight-item'>• Highest-volume scrapped part: <strong>{H(topPart)}</strong> ({topQty:N0} units).</div>");
                }
            }
            else
            {
                sb.Append("<div class='insight-item'>• No scrap data found for the selected filters and date range.</div>");
            }
            sb.Append("</div>");

            // ════ SECTION 1 — EXECUTIVE SUMMARY ══════════════════════════
            sb.Append("<div class='page-break'></div>");
            SectionHead(sb, "1", "EXECUTIVE SUMMARY");

            // KPI Grid
            sb.Append("<div class='kpi-grid'>");
            sb.Append(KpiCard("TOTAL ITEMS", total.ToString("N0"), $"{qty:N0} units", "c-primary"));
            sb.Append(KpiCard("COST IMPACT", $"R {cost:N2}", $"Avg R {(total > 0 ? cost / total : 0):N0}/item", "c-danger"));
            sb.Append(KpiCard("APPROVAL RATE", $"{approvalRate}%", $"{approved} of {total} approved", "c-success"));
            sb.Append(KpiCard("REJECTION RATE", $"{rejectionRate}%", $"{rejected} items rejected", "c-warning"));
            sb.Append(KpiCard("TOTAL WEIGHT", $"{totalWt:N2} kg", $"{weighings} weighing records", "c-primary"));
            sb.Append(KpiCard("PENDING", pending.ToString("N0"), "awaiting approval", "c-warning"));
            sb.Append(KpiCard("APPROVED", approved.ToString("N0"), "fully approved", "c-success"));
            sb.Append(KpiCard("REJECTED", rejected.ToString("N0"), "rejected in workflow", "c-danger"));
            sb.Append("</div>");

            // Approval workflow table
            sb.Append("<h4 style='margin:16px 0 8px;font-size:10pt;color:#1a1a2e;'>Approval Workflow Status</h4>");
            sb.Append("<table><thead><tr><th>Approval Stage</th><th class='r'>Count</th><th class='r'>% of Total</th></tr></thead><tbody>");
            int gTotal = approvalData.Sum(x => (int)(x.Count ?? 0));
            foreach (var row in approvalData)
            {
                string stage = row.ApprovalStage?.ToString() ?? "";
                int cnt = (int)(row.Count ?? 0);
                double pct = gTotal > 0 ? Math.Round(cnt * 100.0 / gTotal, 1) : 0;
                string cls = stage.Contains("Approved") ? "approved" : stage.Contains("Rejected") ? "rejected" : "pending";
                sb.Append($"<tr><td class='{cls}'>{H(stage)}</td><td class='r bold'>{cnt:N0}</td><td class='r'>{pct}%</td></tr>");
            }
            sb.Append("</tbody></table>");

            // ════ SECTION 2 — SCRAP ANALYSIS ═════════════════════════════
            sb.Append("<div class='page-break'></div>");
            SectionHead(sb, "2", "SCRAP ANALYSIS");

            sb.Append("<h4 style='margin-bottom:8px;font-size:10pt;color:#1a1a2e;'>Top 10 Most Scrapped Parts</h4>");
            sb.Append("<table><thead><tr><th>#</th><th>Part Number</th><th>Description</th><th class='r'>Qty</th><th class='r'>Occurrences</th><th class='r'>Est. Cost</th></tr></thead><tbody>");
            int rank = 1;
            foreach (var row in topParts)
            {
                string cls = rank <= 3 ? "top3" : "";
                sb.Append($"<tr><td class='{cls}'>{rank}</td><td class='{cls}'>{H(row.PartNumber?.ToString() ?? "")}</td><td>{H(Trunc(row.Description?.ToString() ?? "", 50))}</td><td class='r {cls}'>{(double)(row.TotalQuantity ?? 0):N0}</td><td class='r'>{(int)(row.OccurrenceCount ?? 0):N0}</td><td class='r'>R {(double)(row.TotalCost ?? 0):N2}</td></tr>");
                rank++;
            }
            sb.Append("</tbody></table>");

            sb.Append("<hr class='divider'>");
            sb.Append("<h4 style='margin-bottom:8px;font-size:10pt;color:#1a1a2e;'>Scrap by Reason</h4>");
            sb.Append("<table><thead><tr><th>Scrap Reason</th><th class='r'>Count</th><th class='r'>Total Qty</th><th class='r'>% of Total</th></tr></thead><tbody>");
            int gReason = byReason.Sum(x => (int)(x.Count ?? 0));
            foreach (var row in byReason)
            {
                int cnt = (int)(row.Count ?? 0);
                double pct = gReason > 0 ? Math.Round(cnt * 100.0 / gReason, 1) : 0;
                sb.Append($"<tr><td>{H(row.ScrapReason?.ToString() ?? "Unknown")}</td><td class='r bold'>{cnt:N0}</td><td class='r'>{(double)(row.TotalQuantity ?? 0):N0}</td><td class='r'>{pct}%</td></tr>");
            }
            sb.Append("</tbody></table>");

            // ════ SECTION 3 — LOCATION & SHIFT ═══════════════════════════
            sb.Append("<div class='page-break'></div>");
            SectionHead(sb, "3", "LOCATION & SHIFT BREAKDOWN");

            sb.Append("<h4 style='margin-bottom:8px;font-size:10pt;color:#1a1a2e;'>Scrap by Production Line</h4>");
            sb.Append("<table><thead><tr><th>Line</th><th class='r'>Items</th><th class='r'>Total Qty</th><th class='r'>Est. Cost</th><th class='r'>Approved</th></tr></thead><tbody>");
            foreach (var row in byLine)
            {
                int appCnt = (int)(row.ApprovedCount ?? 0);
                string appCls = appCnt > 0 ? "approved" : "";
                sb.Append($"<tr><td>{H(row.Line?.ToString() ?? "Unknown")}</td><td class='r bold'>{(int)(row.Count ?? 0):N0}</td><td class='r'>{(double)(row.TotalQuantity ?? 0):N0}</td><td class='r'>R {(double)(row.TotalCost ?? 0):N2}</td><td class='r {appCls}'>{appCnt:N0}</td></tr>");
            }
            sb.Append("</tbody></table>");

            sb.Append("<hr class='divider'>");
            sb.Append("<h4 style='margin-bottom:8px;font-size:10pt;color:#1a1a2e;'>Scrap by Shift</h4>");
            sb.Append("<table><thead><tr><th>Shift</th><th class='r'>Items</th><th class='r'>Total Qty</th><th class='r'>Est. Cost</th><th class='r'>% of Total</th></tr></thead><tbody>");
            int gShift = byShift.Sum(x => (int)(x.Count ?? 0));
            foreach (var row in byShift)
            {
                int cnt = (int)(row.Count ?? 0);
                double pct = gShift > 0 ? Math.Round(cnt * 100.0 / gShift, 1) : 0;
                sb.Append($"<tr><td class='bold'>{H(row.Shift?.ToString() ?? "Unknown")}</td><td class='r bold'>{cnt:N0}</td><td class='r'>{(double)(row.TotalQuantity ?? 0):N0}</td><td class='r'>R {(double)(row.TotalCost ?? 0):N2}</td><td class='r'>{pct}%</td></tr>");
            }
            sb.Append("</tbody></table>");

            // ════ SECTION 4 — DETAILED ITEMS ═════════════════════════════
            if (scrapData.Any())
            {
                sb.Append("<div class='page-break'></div>");
                SectionHead(sb, "4", $"DETAILED SCRAP ITEMS  ({scrapData.Count:N0} records)");

                sb.Append("<table class='detail-table'><thead><tr><th>Part #</th><th>Dept / Line</th><th class='r'>Qty</th><th class='r'>Cost</th><th>Submitted By</th><th>Date</th><th>Status</th><th>Reason</th></tr></thead><tbody>");
                foreach (var row in scrapData)
                {
                    string status = row.Status?.ToString() ?? "";
                    string stCls = status == "Approved" ? "approved" : status == "Rejected" ? "rejected" : "pending";
                    string dateStr = row.SubmissionDate != null
                        ? ((DateTime?)row.SubmissionDate).Value.ToString("dd/MM/yy") : "";
                    sb.Append($"<tr><td class='bold'>{H(row.PartNumber?.ToString() ?? "")}</td><td>{H(row.Department?.ToString() ?? "")}/{H(row.Line?.ToString() ?? "")}</td><td class='r bold'>{(double)(row.Quantity ?? 0):N0}</td><td class='r'>R {(double)(row.TotalCost ?? 0):N0}</td><td>{H(row.SubmittedBy?.ToString() ?? "")}</td><td>{dateStr}</td><td class='{stCls}'>{H(status)}</td><td>{H(Trunc(row.ErrorName?.ToString() ?? "", 30))}</td></tr>");
                }
                sb.Append("</tbody></table>");
            }

            // Footer
            sb.Append($@"
<div class='report-footer'>
  <span>HESTO Harnesses — Scrap Intelligence Report — {dept} / {line} / {shift}</span>
  <span>{start:dd MMM yyyy} – {end.AddDays(-1):dd MMM yyyy}  |  Generated {DateTime.Now:dd MMM yyyy HH:mm}</span>
</div>
</div>
<script>
  // Auto-open print dialog
  window.addEventListener('load', function() {{
    setTimeout(function() {{
      // Only auto-print if opened from the app (not direct URL)
      if (document.referrer) window.print();
    }}, 800);
  }});
</script>
</body></html>");

            return sb.ToString();
        }

        // ── HTML snippet helpers ─────────────────────────────────────────
        private static string InfoCell(string label, string value)
            => $"<div class='info-cell'><div class='info-label'>{label}</div><div class='info-value'>{H(value)}</div></div>";

        private static string StatBox(string label, string value, string cls)
            => $"<div class='stat-box {cls}'><div class='stat-num'>{value}</div><div class='stat-lbl'>{label}</div></div>";

        private static string KpiCard(string name, string val, string sub, string cls)
            => $"<div class='kpi-card {cls}'><div class='kpi-val'>{val}</div><div class='kpi-name'>{name}</div><div class='kpi-sub'>{sub}</div></div>";

        private static void SectionHead(StringBuilder sb, string num, string title)
        {
            sb.Append($"<div class='section-header'><div class='section-bar'></div><div class='section-title'>SECTION {num}  —  {H(title)}</div></div>");
        }

        // ══════════════════════════════════════════════════════════════════
        //  DATA FETCHERS
        // ══════════════════════════════════════════════════════════════════
        //  DATA FETCHERS
        // ══════════════════════════════════════════════════════════════════

        // One row per scrap record — prevents double counting when Syspro has
        // stock codes that differ only by case (e.g. EBD0532A / EBD0532a)
        // InvMaster with case/space duplicates removed (e.g. EBD0532A / EBD0532a),
        // done once up front so SQL can use a fast hash join
        // Plain fast join. Excludes the duplicate-case stock codes in Syspro
        // (EBD0532a, AVf30B/R, " 4KB-927680-501") so no scrap row is counted twice.
        // Remove the NOT IN once Ettienne has cleaned these up in Syspro.
        private const string InvMasterApply = @"
LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im
    ON s.PartNumber = im.StockCode COLLATE Latin1_General_CI_AS
   AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')";

        private async Task<IEnumerable<dynamic>> FetchScrapData(
            DateTime start, DateTime end, string dept, string line, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT
    s.ScrapId, s.PartNumber,
    ISNULL(im.LongDesc,'') AS Description,
    s.Quantity, ISNULL(im.StockUom,'EA') AS StockUom,
    ISNULL(im.MaterialCost,0) AS MaterialCost,
    ISNULL(im.MaterialCost,0) * s.Quantity AS TotalCost,
    s.Department, s.Line, s.Shift, s.Status,
    s.ClockNumber AS SubmittedBy,
    s.SubmissionDate, s.CreatedAt,
    s.ScrapReason AS ErrorName,
    s.TeamLeaderApprovalStatus,
    s.GroupLeaderApprovalStatus,
    s.ManagerApprovalStatus,
    s.RejectedReason
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s" + InvMasterApply + @"
WHERE s.CreatedAt >= @Start AND s.CreatedAt < @End
  AND (@Dept  IS NULL OR s.Department = @Dept)
  AND (@Line  IS NULL OR s.Line = @Line OR s.Line LIKE @Line + ' - %')
  AND (@Shift IS NULL OR s.Shift = @Shift)
ORDER BY s.CreatedAt DESC";
            return await conn.QueryAsync(sql, P(start, end, dept, line, shift));
        }

        private async Task<dynamic?> FetchStats(
            DateTime start, DateTime end, string dept, string line, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT COUNT(*) AS TotalEntries,
    ISNULL(SUM(s.Quantity),0) AS TotalQuantity,
    ISNULL(SUM(ISNULL(im.MaterialCost,0)*s.Quantity),0) AS TotalCost,
    SUM(CASE WHEN s.Status='Approved' THEN 1 ELSE 0 END) AS ApprovedCount,
    SUM(CASE WHEN s.Status='Rejected' THEN 1 ELSE 0 END) AS RejectedCount,
    SUM(CASE WHEN s.Status NOT IN ('Approved','Rejected') THEN 1 ELSE 0 END) AS PendingCount
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s" + InvMasterApply + @"
WHERE s.CreatedAt >= @Start AND s.CreatedAt < @End
  AND (@Dept  IS NULL OR s.Department = @Dept)
  AND (@Line  IS NULL OR s.Line = @Line OR s.Line LIKE @Line + ' - %')
  AND (@Shift IS NULL OR s.Shift = @Shift)";
            return await conn.QueryFirstOrDefaultAsync(sql, P(start, end, dept, line, shift));
        }

        private async Task<IEnumerable<dynamic>> FetchTopParts(
            DateTime start, DateTime end, string dept, string line, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT TOP 10 s.PartNumber,
    ISNULL(im.LongDesc,'No description') AS Description,
    SUM(s.Quantity) AS TotalQuantity,
    COUNT(*) AS OccurrenceCount,
    ISNULL(SUM(im.MaterialCost*s.Quantity),0) AS TotalCost
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s" + InvMasterApply + @"
WHERE s.CreatedAt >= @Start AND s.CreatedAt < @End
  AND (@Dept  IS NULL OR s.Department = @Dept)
  AND (@Line  IS NULL OR s.Line = @Line OR s.Line LIKE @Line + ' - %')
  AND (@Shift IS NULL OR s.Shift = @Shift)
GROUP BY s.PartNumber, im.LongDesc
ORDER BY SUM(s.Quantity) DESC";
            return await conn.QueryAsync(sql, P(start, end, dept, line, shift));
        }

        private async Task<IEnumerable<dynamic>> FetchByLine(
            DateTime start, DateTime end, string dept, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT TOP 15 s.Line, COUNT(*) AS Count,
    SUM(s.Quantity) AS TotalQuantity,
    ISNULL(SUM(im.MaterialCost*s.Quantity),0) AS TotalCost,
    SUM(CASE WHEN s.Status='Approved' THEN 1 ELSE 0 END) AS ApprovedCount
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s" + InvMasterApply + @"
WHERE s.CreatedAt >= @Start AND s.CreatedAt < @End
  AND (@Dept  IS NULL OR s.Department = @Dept)
  AND (@Shift IS NULL OR s.Shift = @Shift)
GROUP BY s.Line ORDER BY COUNT(*) DESC";
            return await conn.QueryAsync(sql, new
            {
                Start = start,
                End = end,
                Dept = NullIfAll(dept, "All Departments"),
                Shift = NullIfAll(shift, "All Shifts")
            });
        }

        private async Task<IEnumerable<dynamic>> FetchByShift(
            DateTime start, DateTime end, string dept, string line)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT s.Shift, COUNT(*) AS Count,
    SUM(s.Quantity) AS TotalQuantity,
    ISNULL(SUM(im.MaterialCost*s.Quantity),0) AS TotalCost
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s" + InvMasterApply + @"
WHERE s.CreatedAt >= @Start AND s.CreatedAt < @End
  AND (@Dept IS NULL OR s.Department = @Dept)
  AND (@Line IS NULL OR s.Line = @Line OR s.Line LIKE @Line + ' - %')
GROUP BY s.Shift ORDER BY COUNT(*) DESC";
            return await conn.QueryAsync(sql, new
            {
                Start = start,
                End = end,
                Dept = NullIfAll(dept, "All Departments"),
                Line = NullIfAll(line, "All Lines")
            });
        }

        private async Task<IEnumerable<dynamic>> FetchByReason(
            DateTime start, DateTime end, string dept, string line, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT TOP 10 ScrapReason, COUNT(*) AS Count, SUM(Quantity) AS TotalQuantity
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE CreatedAt >= @Start AND CreatedAt < @End
  AND (@Dept  IS NULL OR Department = @Dept)
  AND (@Line  IS NULL OR Line = @Line OR Line LIKE @Line + ' - %')
  AND (@Shift IS NULL OR Shift = @Shift)
GROUP BY ScrapReason ORDER BY COUNT(*) DESC";
            return await conn.QueryAsync(sql, P(start, end, dept, line, shift));
        }

        private async Task<dynamic?> FetchWeightSummary(
            DateTime start, DateTime end, string dept, string line, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT COUNT(*) AS TotalWeighings,
    ISNULL(SUM(Weight),0) AS TotalWeight,
    ISNULL(AVG(Weight),0) AS AvgWeight,
    ISNULL(MAX(Weight),0) AS MaxWeight
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE CreatedAt >= @Start AND CreatedAt < @End
  AND (@Dept  IS NULL OR Department = @Dept)
  AND (@Line  IS NULL OR Line = @Line OR Line LIKE @Line + ' - %')
  AND (@Shift IS NULL OR Shift = @Shift)";
            return await conn.QueryFirstOrDefaultAsync(sql, P(start, end, dept, line, shift));
        }

        private async Task<IEnumerable<dynamic>> FetchApprovalSummary(
            DateTime start, DateTime end, string dept, string line, string shift)
        {
            using var conn = new SqlConnection(_connectionString);
            const string sql = @"
SELECT
    CASE Status
        WHEN 'Approved'               THEN 'Fully Approved'
        WHEN 'Rejected'               THEN 'Rejected'
        WHEN 'Pending TL'             THEN 'Pending Team Leader'
        WHEN 'Pending GL'             THEN 'Pending Group Leader'
        WHEN 'Pending Manager'        THEN 'Pending Manager'
        WHEN 'Pending Senior Manager' THEN 'Pending Senior Manager'
        ELSE 'Other'
    END AS ApprovalStage,
    COUNT(*) AS Count
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE CreatedAt >= @Start AND CreatedAt < @End
  AND (@Dept  IS NULL OR Department = @Dept)
  AND (@Line  IS NULL OR Line = @Line OR Line LIKE @Line + ' - %')
  AND (@Shift IS NULL OR Shift = @Shift)
GROUP BY Status
ORDER BY MIN(CASE Status
    WHEN 'Pending TL'             THEN 1
    WHEN 'Pending GL'             THEN 2
    WHEN 'Pending Manager'        THEN 3
    WHEN 'Pending Senior Manager' THEN 4
    WHEN 'Approved'               THEN 5
    WHEN 'Rejected'               THEN 6
    ELSE 7 END)";
            return await conn.QueryAsync(sql, P(start, end, dept, line, shift));
        }

        // ── Parameter helpers ────────────────────────────────────────────
        private static object P(DateTime start, DateTime end,
            string dept, string line, string shift) => new
            {
                Start = start,
                End = end,
                Dept = NullIfAll(dept, "All Departments"),
                Line = NullIfAll(line, "All Lines"),
                Shift = NullIfAll(shift, "All Shifts")
            };

        private static (DateTime, DateTime) ResolveDates(DateTime? s, DateTime? e)
     => (s ?? DateTime.Today.AddDays(-30), (e ?? DateTime.Today).AddDays(1));

        private static string? NullIfAll(string v, string sentinel)
            => (string.IsNullOrWhiteSpace(v) || v == sentinel) ? null : v;

        private static string Safe(string s)
            => System.Text.RegularExpressions.Regex.Replace(s ?? "Report", @"[^a-zA-Z0-9]", "_");

        private static string Trunc(string s, int max)
            => s.Length > max ? s[..max] + "…" : s;

        // HTML-encode to prevent XSS in the report
        private static string H(string s)
            => System.Web.HttpUtility.HtmlEncode(s ?? "");
    }
}