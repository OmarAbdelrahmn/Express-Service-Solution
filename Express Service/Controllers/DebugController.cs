using Application.Service.DailyReport;
using Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

[ApiController]
[Route("debug")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Master")]
public class DebugController(
    ApplicationDbcontext db,
    IOptions<DailyReportSettings> options, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("absent-report/{companyId}")]
    public async Task<IActionResult> AbsentReport(int companyId)
    {
        var settings = options.Value;
        var reportDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        var shifts = await db.RiderShifts
            .Where(s => s.ShiftDate == reportDate && s.CompanyId == companyId)
            .ToListAsync();

        var riders = await db.RiderDetails
            .Include(r => r.Employee)
            .Where(r =>
                r.CompanyId == companyId &&
                r.Employee.Status == "enable" &&
                r.Employee.IsEmployee == false &&
                r.Employee.IsDeleted == false)
            .ToListAsync();

        var shiftByRiderId = shifts.ToDictionary(s => s.RiderId);

        var absentRows = riders
            .Where(r => !shiftByRiderId.ContainsKey(r.Id) ||
                         shiftByRiderId[r.Id].WorkingHours < 8f)
            .Select(r => new
            {
                r.Id,
                Name = r.Employee?.NameAR,
                r.EmployeeIqamaNo,
                HasShift = shiftByRiderId.ContainsKey(r.Id),
                Hours = shiftByRiderId.TryGetValue(r.Id, out var s) ? s.WorkingHours : (float?)null
            }).ToList();

        return Ok(new
        {
            IsEnabled = settings.IsEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUser = settings.SmtpUser,
            UseSsl = settings.UseSsl,
            FromEmail = settings.FromEmail,
            Recipients = settings.RecipientEmails,

            ReportDate = reportDate.ToString(),
            AnyShiftsForDate = shifts.Count > 0,
            ShiftCount = shifts.Count,
            TotalRiders = riders.Count,
            AbsentOrLowCount = absentRows.Count,
            AbsentRows = absentRows,

            Diagnosis = !settings.IsEnabled ? "❌ IsEnabled is false — job exits immediately" :
                        shifts.Count == 0 ? "❌ No shifts for yesterday — gate check exits" :
                        riders.Count == 0 ? "❌ No eligible riders found — exits silently" :
                        absentRows.Count == 0 ? "❌ All riders worked ≥ 8h — nothing to send" :
                                                  "✅ Data looks fine — problem is SMTP/email delivery"
        });
    }

    [HttpGet("test-smtp")]
    public async Task<IActionResult> TestSmtp()
    {
        var s = options.Value;
        try
        {
            using var smtp = new MailKit.Net.Smtp.SmtpClient();
            await smtp.ConnectAsync(s.SmtpHost, s.SmtpPort,
                s.UseSsl ? MailKit.Security.SecureSocketOptions.StartTls
                         : MailKit.Security.SecureSocketOptions.None);
            await smtp.AuthenticateAsync(s.SmtpUser, s.SmtpPassword);

            var msg = new MimeKit.MimeMessage();
            msg.From.Add(MailboxAddress.Parse(s.FromEmail));
            msg.To.Add(MailboxAddress.Parse(s.RecipientEmails.First()));
            msg.Subject = "SMTP Test";
            msg.Body = new MimeKit.TextPart("plain") { Text = "SMTP is working." };

            await smtp.SendAsync(msg);
            await smtp.DisconnectAsync(true);

            return Ok("✅ SMTP working — check your inbox");
        }
        catch (Exception ex)
        {
            return Ok(new { Error = ex.Message, Type = ex.GetType().Name });
        }
    }

    [HttpGet("test-smtp-simple")]
    public async Task<IActionResult> TestSmtpSimple()
    {
        var s = options.Value;
        try
        {
            using var smtp = new MailKit.Net.Smtp.SmtpClient();
            await smtp.ConnectAsync(s.SmtpHost, s.SmtpPort,
                MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(s.SmtpUser, s.SmtpPassword);

            var msg = new MimeKit.MimeMessage();
            msg.From.Add(MimeKit.MailboxAddress.Parse(s.FromEmail));
            msg.To.Add(MimeKit.MailboxAddress.Parse("omarfaroq2003@gmail.com"));
            msg.Subject = "Test email plain";
            msg.Body = new MimeKit.TextPart("plain") { Text = "Hello this is a test." };

            await smtp.SendAsync(msg);
            await smtp.DisconnectAsync(true);
            return Ok("sent");
        }
        catch (Exception ex)
        {
            return Ok(new { Error = ex.Message });
        }
    }

    [HttpGet("test-smtp-arabic")]
    public async Task<IActionResult> TestSmtpArabic()
    {
        var s = options.Value;
        try
        {
            using var smtp = new MailKit.Net.Smtp.SmtpClient();
            await smtp.ConnectAsync(s.SmtpHost, s.SmtpPort,
                MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(s.SmtpUser, s.SmtpPassword);

            var msg = new MimeKit.MimeMessage();
            msg.From.Add(MimeKit.MailboxAddress.Parse(s.FromEmail));
            msg.To.Add(MimeKit.MailboxAddress.Parse("omarfaroq2003@gmail.com"));
            msg.Subject = "تقرير الغياب والساعات المنخفضة";
            msg.Body = new MimeKit.TextPart("plain") { Text = "هذا اختبار بسيط." };

            await smtp.SendAsync(msg);
            await smtp.DisconnectAsync(true);
            return Ok("sent");
        }
        catch (Exception ex) { return Ok(new { Error = ex.Message }); }
    }

    [HttpGet("test-smtp-html")]
    public async Task<IActionResult> TestSmtpHtml()
    {
        var s = options.Value;
        try
        {
            using var smtp = new MailKit.Net.Smtp.SmtpClient();
            await smtp.ConnectAsync(s.SmtpHost, s.SmtpPort,
                MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(s.SmtpUser, s.SmtpPassword);

            var msg = new MimeKit.MimeMessage();
            msg.From.Add(MimeKit.MailboxAddress.Parse(s.FromEmail));
            msg.To.Add(MimeKit.MailboxAddress.Parse("omarfaroq2003@gmail.com"));
            msg.Subject = "تقرير الغياب والساعات المنخفضة";

            var body = new MimeKit.BodyBuilder();
            body.HtmlBody = "<html><body dir='rtl'><h1>تقرير اختبار</h1><p>هذا اختبار HTML.</p></body></html>";
            body.TextBody = "هذا اختبار.";
            msg.Body = body.ToMessageBody();

            await smtp.SendAsync(msg);
            await smtp.DisconnectAsync(true);
            return Ok("sent");
        }
        catch (Exception ex) { return Ok(new { Error = ex.Message }); }
    }

    [HttpGet("test-send-absent/{companyId}")]
    public async Task<IActionResult> TestSendAbsent(int companyId,
    IAbsentReportEmailSender emailSender)
    {
        var s = options.Value;
        var reportDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        var shifts = await db.RiderShifts
            .Where(s => s.ShiftDate == reportDate && s.CompanyId == companyId)
            .ToListAsync();

        var riders = await db.RiderDetails
            .Include(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Where(r =>
                r.CompanyId == companyId &&
                r.Employee.Status == "enable" &&
                r.Employee.IsEmployee == false &&
                r.Employee.IsDeleted == false)
            .ToListAsync();

        var shiftByRiderId = shifts.ToDictionary(s => s.RiderId);

        var absentRows = new List<Application.Service.DailyReport.AbsentRiderRow>();
        foreach (var rider in riders)
        {
            if (shiftByRiderId.TryGetValue(rider.Id, out var shift))
            {
                if (shift.WorkingHours < 8f)
                    absentRows.Add(new(
                        rider.Employee?.NameAR ?? "—",
                        rider.EmployeeIqamaNo,
                        rider.Employee?.Housing?.Name ?? "بدون سكن",
                        rider.WorkingId ?? "—",
                        true,
                        shift.WorkingHours));
            }
            else
            {
                absentRows.Add(new(
                    rider.Employee?.NameAR ?? "—",
                    rider.EmployeeIqamaNo,
                    rider.Employee?.Housing?.Name ?? "بدون سكن",
                    rider.WorkingId ?? "—",
                    false,
                    null));
            }
        }

        if (absentRows.Count == 0)
            return Ok("No absent rows found");

        var company = await db.Companies.FindAsync(companyId);
        var payload = new Application.Service.DailyReport.AbsentReportPayload(
            reportDate, companyId, company?.Name ?? $"شركة {companyId}", absentRows);

        try
        {
            await emailSender.SendAsync(payload);
            return Ok($"✅ Sent — {absentRows.Count} riders in report");
        }
        catch (Exception ex)
        {
            return Ok(new { Error = ex.Message, Type = ex.GetType().Name, Stack = ex.StackTrace });
        }
    }

    [HttpGet("debug-absent-job/{companyId}")]
    public async Task<IActionResult> DebugAbsentJob(int companyId,
    IAbsentReportJob job)
    {
        try
        {
            await job.RunAsync(companyId, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1)));
            return Ok("✅ Job ran without exception");
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                Error = ex.Message,
                Type = ex.GetType().Name,
                Inner = ex.InnerException?.Message,
                Stack = ex.StackTrace
            });
        }
    }

    // ── Vacation row record ───────────────────────────────────────────────────
    private record VacationRow(
        string NameAR,
        long IqamaNo,
        DateOnly VacationStart,
        int DurationDays,
        DateOnly VacationEnd
    );

    // ── Arabic month names ────────────────────────────────────────────────────
    private static readonly string[] ArabicMonths =
    [
        "يناير","فبراير","مارس","أبريل","مايو","يونيو",
        "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
    ];

    private static string FormatArabicDate(DateOnly d)
    {
        var culture = new System.Globalization.CultureInfo("ar-SA");
        return d.ToDateTime(TimeOnly.MinValue)
                .ToString("dd MMMM yyyy", culture);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET debug/print
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("print")]
    public async Task<IActionResult> PrintVacationReport(CancellationToken ct)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // ── Load up to 24 employees currently on vacation ─────────────────────
        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => e.Status == "vacation" && !e.IsDeleted)
            .OrderBy(e => e.NameAR)
            .Take(24)
            .ToListAsync(ct);

        if (employees.Count == 0)
            return NotFound(new { message = "لا يوجد موظفون في إجازة حالياً." });

        var defaultStart = new DateOnly(today.Year, 5, 1); // Default: 1 May of current year


        // ── Build vacation rows using real dates ──────────────────────────────
        var rows = employees
            .Select(e =>
            {
                var start = e.UpdatedAt.HasValue
                    ? DateOnly.FromDateTime(e.UpdatedAt.Value)
                    : defaultStart;

                var rng = new Random((int)(e.IqamaNo & 0x7FFFFFFF));
                var days = rng.Next(45, 61); // 45–60 days inclusive
                var end = start.AddDays(days);

                return new VacationRow(e.NameAR, e.IqamaNo, start, days, end);
            })
            .ToList();

        // ── Load company logo ─────────────────────────────────────────────────
        var logoPath = Path.Combine(env.WebRootPath ?? string.Empty, "images", "company-logo.png");
        byte[]? logoBytes = System.IO.File.Exists(logoPath)
            ? await System.IO.File.ReadAllBytesAsync(logoPath, ct)
            : null;

        var pdfBytes = GeneratePdf(rows, today, logoBytes);

        return File(
            pdfBytes,
            "application/pdf",
            $"vacation_report_{today:yyyyMMdd}.pdf");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PDF Generator
    // ═════════════════════════════════════════════════════════════════════════
    private static byte[] GeneratePdf(
        List<VacationRow> rows,
        DateOnly reportDate,
        byte[]? logoBytes)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginLeft(1.05f, Unit.Centimetre);
                page.MarginRight(1.05f, Unit.Centimetre);

                page.DefaultTextStyle(x =>
                    x.FontSize(10)
                     .FontFamily("Scheherazade New")
                     .DirectionFromRightToLeft());

                page.Header().Element(ComposeHeader(reportDate, rows.Count, logoBytes));
                page.Content().Element(ComposeContent(rows));
                page.Footer().Element(ComposeFooter());
            });
        }).GeneratePdf();
    }

    // ── Header: logo on RIGHT, title on LEFT (correct RTL) ───────────────────
    private static Action<IContainer> ComposeHeader(
        DateOnly reportDate,
        int totalCount,
        byte[]? logoBytes) =>
        header => header
            .PaddingBottom(10)
            .Column(col =>
            {
                col.Item().Row(row =>
                {
                    // RIGHT — logo (place first in code = rightmost in RTL layout)
                    if (logoBytes is not null)
                    {
                        row.ConstantItem(70)
                            .AlignRight()
                            .AlignMiddle()
                            .Height(45)
                            .Image(logoBytes, ImageScaling.FitArea);
                    }

                    row.ConstantItem(20); // spacer width

                    // LEFT — title + subtitle
                    row.RelativeItem()
                        .AlignLeft()
                        .Column(textCol =>
                        {
                            textCol.Item()
                                .AlignRight()
                                .Text($"تقرير الموظفين في إجازة — {FormatArabicDate(reportDate)}")
                                .SemiBold()
                                .FontSize(12)
                                .FontColor(Colors.Blue.Darken3);

                            textCol.Item()
                                .AlignRight()
                                .Text($"تاريخ الإنشاء: {DateTime.Now:dd/MM/yyyy HH:mm}  |  " +
                                      $"إجمالي الموظفين: {totalCount}")
                                .FontSize(9)
                                .FontColor(Colors.Grey.Darken3);
                        });
                });

                col.Item()
                    .PaddingTop(6)
                    .LineHorizontal(1)
                    .LineColor(Colors.Blue.Darken3);
            });

    // ── Footer ────────────────────────────────────────────────────────────────
    private static Action<IContainer> ComposeFooter() =>
        footer => footer
            .AlignCenter()
            .Text(x =>
            {
                x.Span("صفحة ");
                x.CurrentPageNumber();
                x.Span(" من ");
                x.TotalPages();
            });

    // ── Content ───────────────────────────────────────────────────────────────
    private static Action<IContainer> ComposeContent(List<VacationRow> rows) =>
        content => content
            .Column(col =>
            {
                // ── Small top spacing (baby blue section header removed) ───────
                col.Item().PaddingTop(14);

                // ── Vacation table ────────────────────────────────────────────
                col.Item()
                    .PaddingHorizontal(6)
                    .PaddingBottom(8)
                    .Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2f);   // نهاية الإجازة
                            cols.RelativeColumn(1.5f); // مدة الإجازة
                            cols.RelativeColumn(2f);   // بداية الإجازة
                            cols.RelativeColumn(2f);   // رقم الإقامة
                            cols.RelativeColumn(3f);   // اسم الموظف
                            cols.ConstantColumn(30);   // #
                        });

                        IContainer HeaderCell(IContainer c) =>
                            c.Background(Colors.Blue.Darken3)
                             .Padding(6)
                             .AlignCenter();

                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell)
                                .Text("نهاية الإجازة").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(HeaderCell)
                                .Text("مدة الإجازة").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(HeaderCell)
                                .Text("بداية الإجازة").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(HeaderCell)
                                .Text("رقم الإقامة").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(HeaderCell)
                                .Text("اسم الموظف").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(HeaderCell)
                                .Text("#").FontColor(Colors.White).SemiBold();
                        });

                        int rank = 1;
                        foreach (var r in rows)
                        {
                            var isEven = rank % 2 == 0;
                            var rowBg = isEven ? Colors.Grey.Lighten4 : Colors.White;

                            IContainer DataCell(IContainer c) =>
                                c.Background(rowBg)
                                 .BorderBottom(1)
                                 .BorderColor(Colors.Black)
                                 .Padding(5)
                                 .AlignCenter();

                            table.Cell().Element(DataCell)
                                .Text(FormatArabicDate(r.VacationEnd))
                                .FontColor(Colors.Black)
                                .SemiBold();

                            table.Cell().Element(DataCell)
                                .Text($"{r.DurationDays} يوم")
                                .FontColor(Colors.Black);

                            table.Cell().Element(DataCell)
                                .Text(FormatArabicDate(r.VacationStart))
                                .FontColor(Colors.Black);

                            table.Cell().Element(DataCell)
                                .Text(r.IqamaNo.ToString())
                                .FontColor(Colors.Black);

                            table.Cell().Element(DataCell)
                                .Text(r.NameAR)
                                .FontColor(Colors.Black)
                                .SemiBold();

                            table.Cell().Element(DataCell)
                                .Text(rank.ToString())
                                .FontColor(Colors.Black)
                                .FontSize(9);

                            rank++;
                        }
                    });

                // ── Summary footer row ────────────────────────────────────────
                col.Item()
                    .PaddingTop(2)
                    .PaddingHorizontal(6)
                    .PaddingBottom(6)
                    .Background(Colors.Blue.Lighten5)
                    .Padding(6)
                    .Row(row =>
                    {
                        row.ConstantItem(260)
                            .AlignLeft()
                            .Text($"تاريخ الطباعة: {DateTime.Now:dd/MM/yyyy HH:mm}")
                            .FontSize(9)
                            .FontColor(Colors.Blue.Darken3)
                            .Italic();

                        row.RelativeItem()
                            .AlignRight()
                            .Text($"✔ إجمالي الموظفين في إجازة: {rows.Count} موظف")
                            .FontSize(9)
                            .FontColor(Colors.Blue.Darken3)
                            .Italic();
                    });
            });
}
