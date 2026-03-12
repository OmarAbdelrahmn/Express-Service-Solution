using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace Application.Service.DailyReport;

public interface IDailyReportEmailSender
{
    Task SendAsync(
        DailyReportPayload payload,
        byte[] pdfBytes,
        byte[]? logoBytes = null,
        CancellationToken ct = default);
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

    // ── CSS ───────────────────────────────────────────────────────────────────
    // All two-column layouts use <table> — flexbox is unreliable in email clients.
    // Every block explicitly carries direction:rtl to survive Gmail's CSS stripping.
    private const string EmailCss = """
        <style>
        * { box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
            font-size: 13px;
            color: #333;
            background: #f9f9f9;
            direction: rtl;
            text-align: right;
            margin: 0;
            padding: 0;
        }
        .wrap {
            max-width: 860px;
            margin: 20px auto;
            background: #fff;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 2px 8px rgba(0,0,0,.1);
            direction: rtl;
        }
        /* ── Top bar ── */
        .topbar {
            background: #1a3c6e;
            color: #fff;
            padding: 0;
            direction: rtl;
        }
        .topbar table {
            margin: 0;
            border-collapse: collapse;
            direction: rtl;
        }
        .topbar-logo {
            width: 52px;
            height: 52px;
            border-radius: 8px;
            display: block;
        }
        /* ── Body ── */
        .body {
            padding: 20px 24px;
            direction: rtl;
            text-align: right;
        }
        .body p {
            direction: rtl;
            text-align: right;
            margin: 0 0 12px 0;
            line-height: 1.7;
        }
        /* ── PDF note ── */
        .pdf-note {
            background: #fff8e1;
            border: 1px solid #ffe082;
            border-radius: 4px;
            padding: 8px 12px;
            font-size: 12px;
            color: #795548;
            margin-bottom: 16px;
            text-align: right;
            direction: rtl;
        }
        /* ── Company block ── */
        .company-block {
            margin-bottom: 30px;
        }
        /* ── Section labels ── */
        .section-label {
            font-size: 13px;
            font-weight: bold;
            padding: 6px 10px;
            margin: 10px 0 4px;
            border-radius: 3px;
            text-align: right;
            direction: rtl;
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
        /* ── Data tables ── */
        table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 6px;
            direction: rtl;
        }
        th {
            padding: 8px 10px;
            text-align: right;
            font-size: 12px;
        }
        th.th-num { text-align: center; }
        td {
            padding: 7px 10px;
            text-align: right;
            border-bottom: 1px solid #eee;
            font-size: 12px;
        }
        td.num { text-align: center; }
        tr:nth-child(even) td { background: #f5f7fa; }
        /* ── Top table header / values ── */
        .th-top  { background: #2e7d32; color: #fff; }
        .th-bot  { background: #c62828; color: #fff; }
        .val-top { color: #2e7d32; font-weight: bold; }
        .val-bot { color: #c62828; font-weight: bold; }
        /* ── Footer ── */
        .footer {
            background: #f0f0f0;
            text-align: center;
            padding: 12px;
            font-size: 11px;
            color: #888;
            direction: rtl;
        }
        </style>
        """;

    // ─────────────────────────────────────────────────────────────────────────

    public async Task SendAsync(
        DailyReportPayload payload,
        byte[] pdfBytes,
        byte[]? logoBytes = null,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));

        foreach (var email in _settings.RecipientEmails)
            message.To.Add(MailboxAddress.Parse(email));

        message.Subject = $"📊 تقرير الأداء اليومي — {FormatArabicDate(payload.ReportDate)}";
        message.Headers.Add("Content-Language", "ar");

        var body = new BodyBuilder();

        // ── Embed logo as CID inline resource ────────────────────────────────
        string? logoCid = null;
        if (logoBytes is not null)
        {
            var logoResource = body.LinkedResources.Add(
                "company-logo.png",
                logoBytes,
                new ContentType("image", "png"));
            logoResource.ContentId = MimeUtils.GenerateMessageId();
            logoCid = logoResource.ContentId;
        }

        body.HtmlBody = BuildHtmlBody(payload, logoCid);
        body.TextBody = BuildTextBody(payload);

        body.Attachments.Add(
            $"تقرير_الأداء_{payload.ReportDate:yyyyMMdd}.pdf",
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

        sb.AppendLine($"تقرير الأداء اليومي — {FormatArabicDate(payload.ReportDate)}");
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
        sb.AppendLine("* يُرجى مراجعة ملف PDF المرفق للاطلاع على تقرير جميع المناديب.");
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
    private static string BuildHtmlBody(DailyReportPayload payload, string? logoCid)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("<html lang=\"ar\" dir=\"rtl\">");
        sb.Append("<head><meta charset=\"UTF-8\"/>");
        sb.Append(EmailCss);
        sb.Append("</head>");
        sb.Append("<body dir=\"rtl\" style=\"direction:rtl;text-align:right;margin:0;padding:0\">");
        sb.Append("<div class=\"wrap\">");

        // ── Top bar ───────────────────────────────────────────────────────────
        // Table layout: logo on LEFT cell, title on RIGHT cell (RTL = right is primary)
        sb.Append("<div class=\"topbar\">");
        sb.Append("<table width=\"100%\" cellpadding=\"18\" cellspacing=\"0\" border=\"0\" style=\"direction:rtl\">");
        sb.Append("<tr>");

        // RIGHT cell — main title (primary in RTL)
        sb.Append("<td align=\"right\" valign=\"middle\" style=\"padding:18px 24px\">");
        sb.Append("<h1 style=\"margin:0 0 5px 0;font-size:19px;color:#fff;font-weight:bold\">📊 تقرير الأداء اليومي</h1>");
        sb.Append($"<p style=\"margin:0;font-size:12px;color:#fff;opacity:.85\">");
        sb.Append($"التاريخ: {FormatArabicDate(payload.ReportDate)} &nbsp;|&nbsp; إجمالي الورديات: {payload.GrandTotalShifts}");
        sb.Append("</p>");
        sb.Append("</td>");

        // LEFT cell — logo (secondary in RTL)
        sb.Append("<td width=\"80\" align=\"center\" valign=\"middle\" style=\"padding:12px 16px\">");
        if (logoCid is not null)
        {
            sb.Append($"<img src=\"cid:{logoCid}\" class=\"topbar-logo\" width=\"52\" height=\"52\" alt=\"\" style=\"display:block;border-radius:8px;background:#fff\"/>");
        }
        else
        {
            // Fallback icon box when no logo is provided
            sb.Append("<div style=\"width:52px;height:52px;background:#fff;border-radius:8px;text-align:center;line-height:52px;font-size:26px\">📦</div>");
        }
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // topbar

        // ── Body ──────────────────────────────────────────────────────────────
        sb.Append("<div class=\"body\">");
        sb.Append("<p>السادة المسؤولين،<br/>فيما يلي ملخص أعلى 5 وأدنى 5 مندوب لكل شركة.</p>");
        sb.Append("<div class=\"pdf-note\">📎 يُرفق ملف PDF يحتوي على بيانات جميع المناديب مُجمَّعةً حسب الشركة والسكن.</div>");

        foreach (var company in payload.Companies)
        {
            var allRows = company.RowsByHousing.Values.SelectMany(r => r).ToList();
            var totalOrders = allRows.Sum(r => r.AcceptedOrders);
            var totalHours = allRows.Sum(r => r.WorkingHours);

            var (top5, bottom5) = GetTopBottom(allRows);

            sb.Append("<div class=\"company-block\">");

            // ── Company header — table layout ─────────────────────────────────
            sb.Append("<table width=\"100%\" cellpadding=\"10\" cellspacing=\"0\" border=\"0\"");
            sb.Append(" style=\"background:#dce8f7;border-right:5px solid #1a3c6e;border-radius:4px;margin-bottom:10px;direction:rtl\">");
            sb.Append("<tr>");
            // RIGHT — company name (primary)
            sb.Append("<td align=\"right\" style=\"font-size:15px;font-weight:bold;color:#1a3c6e\">");
            sb.Append($"🏢 {company.CompanyName}");
            sb.Append("</td>");
            // LEFT — stats (secondary)
            sb.Append("<td align=\"left\" style=\"font-size:12px;font-weight:normal;color:#555;white-space:nowrap\">");
            sb.Append($"ورديات: {company.TotalShifts} &nbsp;|&nbsp; إجمالي الطلبات: {totalOrders}");
            sb.Append("</td>");
            sb.Append("</tr></table>");

            // Top 5
            sb.Append("<div class=\"section-label label-top\">🏆 أعلى 5 مندوب</div>");
            sb.Append(BuildSectionTable(top5, isTop: true));

            // Bottom 5
            sb.Append("<div class=\"section-label label-bottom\">⚠️ أدنى 5 مندوب</div>");
            sb.Append(BuildSectionTable(bottom5, isTop: false));

            // ── Company footer — table layout ─────────────────────────────────
            sb.Append("<table width=\"100%\" cellpadding=\"6\" cellspacing=\"0\" border=\"0\"");
            sb.Append(" style=\"border-top:1px dashed #b0c4de;margin-top:6px;direction:rtl\">");
            sb.Append("<tr>");
            // RIGHT — primary label
            sb.Append("<td align=\"right\" style=\"font-size:11px;color:#1a3c6e;font-style:italic\">");
            sb.Append($"✔ إجمالي الورديات: {company.TotalShifts} وردية");
            sb.Append("</td>");
            // LEFT — numbers
            sb.Append("<td align=\"left\" style=\"font-size:11px;color:#1a3c6e;font-style:italic;white-space:nowrap\">");
            sb.Append($"إجمالي الطلبات: <strong>{totalOrders}</strong> &nbsp;|&nbsp; إجمالي الساعات: <strong>{totalHours:F1}</strong> ساعة");
            sb.Append("</td>");
            sb.Append("</tr></table>");

            sb.Append("</div>"); // company-block
        }

        sb.Append("</div>"); // body

        // ── Footer ────────────────────────────────────────────────────────────
        sb.Append("<div class=\"footer\">");
        sb.Append($"تم إرسال هذا التقرير تلقائيًا بتاريخ {DateTime.Now:dd/MM/yyyy} الساعة {DateTime.Now:HH:mm}");
        sb.Append(" &nbsp;|&nbsp; لا تردَّ على هذا البريد");
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

        var thClass = isTop ? "th-top" : "th-bot";
        var valClass = isTop ? "val-top" : "val-bot";

        var sb = new System.Text.StringBuilder();

        // direction:rtl on the table keeps column order correct in all clients
        sb.Append("<table style=\"direction:rtl\">");
        sb.Append("<thead><tr>");
        sb.Append($"<th class=\"{thClass} th-num\">#</th>");
        sb.Append($"<th class=\"{thClass}\">اسم المندوب</th>");
        sb.Append($"<th class=\"{thClass}\">السكن</th>");
        sb.Append($"<th class=\"{thClass} th-num\">الطلبات المقبولة</th>");
        sb.Append($"<th class=\"{thClass} th-num\">ساعات العمل</th>");
        sb.Append("</tr></thead><tbody>");

        int rank = 1;
        foreach (var r in rows)
        {
            sb.Append("<tr>");
            // rank — centered number
            sb.Append($"<td class=\"num\" style=\"color:#999;font-size:11px\">{rank}</td>");
            // name — right aligned
            sb.Append($"<td style=\"text-align:right\"><strong>{r.RiderNameAR}</strong></td>");
            // housing — right aligned
            sb.Append($"<td style=\"text-align:right\">{r.HousingName}</td>");
            // orders — centered, colored
            sb.Append($"<td class=\"num {valClass}\">{r.AcceptedOrders}</td>");
            // hours — centered
            sb.Append($"<td class=\"num\">{r.WorkingHours:F1} ساعة</td>");
            sb.Append("</tr>");
            rank++;
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}