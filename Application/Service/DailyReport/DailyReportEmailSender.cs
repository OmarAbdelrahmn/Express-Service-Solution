using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Application.Service.DailyReport;

public interface IDailyReportEmailSender
{
    Task SendAsync(DailyReportPayload payload, byte[] pdfBytes, CancellationToken ct = default);
}

public class DailyReportEmailSender(IOptions<DailyReportSettings> options) : IDailyReportEmailSender
{
    private readonly DailyReportSettings _settings = options.Value;

    private static readonly string[] ArabicMonths =
    [
        "يناير","فبراير","مارس","أبريل","مايو","يونيو",
        "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
    ];

    private static string FormatArabicDate(DateOnly d) =>
        $"{d.Day} {ArabicMonths[d.Month - 1]} {d.Year}";

    // ── CSS lives here — no interpolation, no escaping needed ────────────────
    private const string EmailCss = """
        <style>
        body {
            font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
            font-size: 13px;
            color: #333;
            background: #f9f9f9;
            direction: rtl;
            text-align: right;
        }
        .wrap {
            max-width: 860px;
            margin: 20px auto;
            background: #fff;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 2px 8px rgba(0,0,0,.1);
        }
        .topbar {
            background: #1a3c6e;
            color: #fff;
            padding: 18px 24px;
        }
        .topbar h1 {
            margin: 0;
            font-size: 20px;
        }
        .topbar p {
            margin: 4px 0 0;
            font-size: 12px;
            opacity: .8;
        }
        .body {
            padding: 20px 24px;
        }
        .company-block {
            margin-bottom: 30px;
        }
        .company-header {
            background: #dce8f7;
            border-right: 5px solid #1a3c6e;
            padding: 10px 14px;
            margin-bottom: 10px;
            border-radius: 4px;
            font-size: 15px;
            font-weight: bold;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .company-header span {
            font-size: 12px;
            font-weight: normal;
            color: #555;
        }
        .section-label {
            font-size: 13px;
            font-weight: bold;
            padding: 6px 8px;
            margin: 10px 0 4px;
            border-radius: 3px;
        }
        .label-top {
            background: #e8f5e9;
            color: #2e7d32;
            border-right: 4px solid #2e7d32;
        }
        .label-bottom {
            background: #ffebee;
            color: #c62828;
            border-right: 4px solid #c62828;
        }
        table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 6px;
        }
        th {
            padding: 8px 10px;
            text-align: center;
            font-size: 12px;
        }
        td {
            padding: 7px 10px;
            text-align: center;
            border-bottom: 1px solid #eee;
            font-size: 12px;
        }
        tr:nth-child(even) td {
            background: #f5f7fa;
        }
        .th-top {
            background: #2e7d32;
            color: #fff;
        }
        .th-bottom {
            background: #c62828;
            color: #fff;
        }
        .val-top {
            color: #2e7d32;
            font-weight: bold;
        }
        .val-bottom {
            color: #c62828;
            font-weight: bold;
        }
        .company-footer {
            font-size: 11px;
            color: #1a3c6e;
            font-style: italic;
            padding: 6px 8px 4px;
            display: flex;
            justify-content: space-between;
            border-top: 1px dashed #b0c4de;
            margin-top: 6px;
        }
        .pdf-note {
            background: #fff8e1;
            border: 1px solid #ffe082;
            border-radius: 4px;
            padding: 8px 12px;
            font-size: 12px;
            color: #795548;
            margin-bottom: 16px;
        }
        .footer {
            background: #f0f0f0;
            text-align: center;
            padding: 12px;
            font-size: 11px;
            color: #888;
        }
        </style>
        """;

    // ─────────────────────────────────────────────────────────────────────────

    public async Task SendAsync(DailyReportPayload payload, byte[] pdfBytes, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));

        foreach (var email in _settings.RecipientEmails)
            message.To.Add(MailboxAddress.Parse(email));

        message.Subject = $"📊 تقرير الورديات اليومي — {FormatArabicDate(payload.ReportDate)}";
        message.Headers.Add("Content-Language", "ar");

        var body = new BodyBuilder
        {
            HtmlBody = BuildHtmlBody(payload),
            TextBody = BuildTextBody(payload)
        };

        body.Attachments.Add(
            $"تقرير_الورديات_{payload.ReportDate:yyyyMMdd}.pdf",
            pdfBytes,
            new ContentType("application", "pdf"));

        message.Body = body.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(
            _settings.SmtpHost,
            _settings.SmtpPort,
            _settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            ct);

        await smtp.AuthenticateAsync(_settings.SmtpUser, _settings.SmtpPassword, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }

    // ── Top / Bottom slice ────────────────────────────────────────────────────
    private static (List<ShiftReportRow> Top, List<ShiftReportRow> Bottom)
        GetTopBottom(List<ShiftReportRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.AcceptedOrders).ToList();
        var top5 = sorted.Take(5).ToList();
        var top5Keys = top5.Select(r => r.IqamaNo).ToHashSet();
        var bottom5 = sorted
            .Where(r => !top5Keys.Contains(r.IqamaNo))
            .TakeLast(5)
            .OrderBy(r => r.AcceptedOrders)
            .ToList();

        return (top5, bottom5);
    }

    // ── Plain-text fallback ───────────────────────────────────────────────────
    private static string BuildTextBody(DailyReportPayload payload)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"تقرير الورديات اليومي — {FormatArabicDate(payload.ReportDate)}");
        sb.AppendLine($"إجمالي الورديات: {payload.GrandTotalShifts}");
        sb.AppendLine(new string('─', 60));

        foreach (var company in payload.Companies)
        {
            var allRows = company.RowsByHousing.Values.SelectMany(r => r).ToList();
            var totalOrders = allRows.Sum(r => r.AcceptedOrders);
            var totalHours = allRows.Sum(r => r.WorkingHours);

            sb.AppendLine();
            sb.AppendLine($"الشركة: {company.CompanyName} | ورديات: {company.TotalShifts} | إجمالي الطلبات: {totalOrders}");
            sb.AppendLine(new string('═', 50));

            var (top5, bottom5) = GetTopBottom(allRows);

            AppendSection(sb, "أعلى 5 مناديب", top5);
            sb.AppendLine();
            AppendSection(sb, "أدنى 5 مناديب", bottom5);

            sb.AppendLine();
            sb.AppendLine($"  إجمالي الطلبات: {totalOrders} | إجمالي الساعات: {totalHours:F1} ساعة");
        }

        sb.AppendLine();
        sb.AppendLine("* يُرجى مراجعة ملف PDF المرفق للاطلاع على تقرير جميع الرُّكَّاب.");
        return sb.ToString();
    }

    private static void AppendSection(System.Text.StringBuilder sb, string title, List<ShiftReportRow> rows)
    {
        sb.AppendLine($"  ── {title} ──");
        sb.AppendLine($"  {"#",-4} {"الاسم",-25} {"السكن",-20} {"الطلبات",-10} {"ساعات العمل"}");
        sb.AppendLine($"  {new string('-', 70)}");

        int rank = 1;
        foreach (var r in rows)
        {
            sb.AppendLine($"  {rank,-4} {r.RiderNameAR,-25} {r.HousingName,-20} {r.AcceptedOrders,-10} {r.WorkingHours:F1} ساعة");
            rank++;
        }
    }

    // ── HTML body ─────────────────────────────────────────────────────────────
    private static string BuildHtmlBody(DailyReportPayload payload)
    {
        var sb = new System.Text.StringBuilder();

        // Open + head (CSS const — no escaping needed)
        sb.Append("<html lang=\"ar\" dir=\"rtl\">");
        sb.Append("<head><meta charset=\"UTF-8\"/>");
        sb.Append(EmailCss);
        sb.Append("</head><body>");
        sb.Append("<div class=\"wrap\">");

        // Top bar
        sb.Append("<div class=\"topbar\">");
        sb.Append("<h1>📊 تقرير الورديات اليومي</h1>");
        sb.Append($"<p>التاريخ: {FormatArabicDate(payload.ReportDate)} &nbsp;|&nbsp; إجمالي الورديات: {payload.GrandTotalShifts}</p>");
        sb.Append("</div>");

        // Body
        sb.Append("<div class=\"body\">");
        sb.Append("<p>السادة المسؤولين،<br/>فيما يلي ملخص أعلى 5 وأدنى 5 مندوب لكل شركة.</p>");
        sb.Append("<div class=\"pdf-note\">📎 يُرفق ملف PDF يحتوي على بيانات جميع الرُّكَّاب مُجمَّعةً حسب الشركة والسكن.</div>");

        foreach (var company in payload.Companies)
        {
            var allRows = company.RowsByHousing.Values.SelectMany(r => r).ToList();
            var totalOrders = allRows.Sum(r => r.AcceptedOrders);
            var totalHours = allRows.Sum(r => r.WorkingHours);

            var (top5, bottom5) = GetTopBottom(allRows);

            sb.Append("<div class=\"company-block\">");

            // Company header
            sb.Append("<div class=\"company-header\">");
            sb.Append($"<span>🏢 {company.CompanyName}</span>");
            sb.Append($"<span>ورديات: {company.TotalShifts} &nbsp;|&nbsp; إجمالي الطلبات: {totalOrders}</span>");
            sb.Append("</div>");

            // Top 5
            sb.Append("<div class=\"section-label label-top\">🏆 أعلى 5 مندوب</div>");
            sb.Append(BuildSectionTable(top5, isTop: true));

            // Bottom 5
            sb.Append("<div class=\"section-label label-bottom\">⚠️ أدنى 5 مندوب</div>");
            sb.Append(BuildSectionTable(bottom5, isTop: false));

            // Company footer
            sb.Append("<div class=\"company-footer\">");
            sb.Append($"<span>✔ إجمالي الورديات: {company.TotalShifts} وردية</span>");
            sb.Append($"<span>إجمالي الطلبات: <strong>{totalOrders}</strong> &nbsp;|&nbsp; إجمالي الساعات: <strong>{totalHours:F1}</strong> ساعة</span>");
            sb.Append("</div>");

            sb.Append("</div>"); // company-block
        }

        sb.Append("</div>"); // body

        // Footer
        sb.Append("<div class=\"footer\">");
        sb.Append($"تم إرسال هذا التقرير تلقائيًا بتاريخ {DateTime.Now:dd/MM/yyyy} الساعة {DateTime.Now:HH:mm} &nbsp;|&nbsp; لا تردَّ على هذا البريد");
        sb.Append("</div>");

        sb.Append("</div>"); // wrap
        sb.Append("</body></html>");

        return sb.ToString();
    }

    // ── One section table (top or bottom) ────────────────────────────────────
    private static string BuildSectionTable(List<ShiftReportRow> rows, bool isTop)
    {
        if (rows.Count == 0)
            return "<p style=\"color:#999;font-size:12px;padding:4px 8px\">لا توجد بيانات كافية</p>";

        var thClass = isTop ? "th-top" : "th-bottom";
        var valClass = isTop ? "val-top" : "val-bottom";

        var sb = new System.Text.StringBuilder();

        sb.Append("<table><thead><tr>");
        sb.Append($"<th class=\"{thClass}\">#</th>");
        sb.Append($"<th class=\"{thClass}\">اسم المندوب</th>");
        sb.Append($"<th class=\"{thClass}\">السكن</th>");
        sb.Append($"<th class=\"{thClass}\">الطلبات المقبولة</th>");
        sb.Append($"<th class=\"{thClass}\">ساعات العمل</th>");
        sb.Append("</tr></thead><tbody>");

        int rank = 1;
        foreach (var r in rows)
        {
            sb.Append("<tr>");
            sb.Append($"<td style=\"color:#999;font-size:11px\">{rank}</td>");
            sb.Append($"<td><strong>{r.RiderNameAR}</strong></td>");
            sb.Append($"<td>{r.HousingName}</td>");
            sb.Append($"<td class=\"{valClass}\">{r.AcceptedOrders}</td>");
            sb.Append($"<td>{r.WorkingHours:F1} ساعة</td>");
            sb.Append("</tr>");
            rank++;
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}