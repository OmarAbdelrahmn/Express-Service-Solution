using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace Application.Service.DailyReport;

public interface IAbsentReportEmailSender
{
    Task SendAsync(
        AbsentReportPayload payload,
        byte[]? logoBytes = null,
        CancellationToken ct = default);
}

public class AbsentReportEmailSender(IOptions<DailyReportSettings> options) : IAbsentReportEmailSender
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
            background: #7b1a1a;
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
        /* ── Summary box ── */
        .summary-box {
            background: #fff3e0;
            border: 1px solid #ffb74d;
            border-radius: 6px;
            padding: 12px 16px;
            margin-bottom: 18px;
            direction: rtl;
        }
        .summary-box table {
            margin: 0;
        }
        .summary-num {
            font-size: 22px;
            font-weight: bold;
            color: #e65100;
        }
        .summary-label {
            font-size: 11px;
            color: #777;
            margin-top: 2px;
        }
        /* ── Data table ── */
        table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 6px;
            direction: rtl;
        }
        th {
            padding: 9px 10px;
            text-align: right;
            font-size: 12px;
            background: #7b1a1a;
            color: #fff;
        }
        th.th-num { text-align: center; }
        td {
            padding: 8px 10px;
            text-align: right;
            border-bottom: 1px solid #eee;
            font-size: 12px;
        }
        td.num { text-align: center; }
        tr:nth-child(even) td { background: #fafafa; }
        .badge-absent {
            background: #ffebee;
            color: #c62828;
            font-weight: bold;
            border-radius: 3px;
            padding: 2px 8px;
            font-size: 11px;
            white-space: nowrap;
        }
        .badge-low {
            background: #fff3e0;
            color: #e65100;
            font-weight: bold;
            border-radius: 3px;
            padding: 2px 8px;
            font-size: 11px;
            white-space: nowrap;
        }
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
        AbsentReportPayload payload,
        byte[]? logoBytes = null,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));

        foreach (var email in _settings.RecipientEmails)
            message.To.Add(MailboxAddress.Parse(email));

        var absentCount = payload.Riders.Count(r => !r.HadShiftButLowHours);
        var lowHoursCount = payload.Riders.Count(r => r.HadShiftButLowHours);

        message.Subject = $"⚠️ تقرير الغياب والساعات المنخفضة — {payload.CompanyName} — {FormatArabicDate(payload.ReportDate)}";
        message.Headers.Add("Content-Language", "ar");

        var body = new BodyBuilder();

        // ── Embed logo ────────────────────────────────────────────────────────
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

    // ── Plain-text fallback ───────────────────────────────────────────────────
    private static string BuildTextBody(AbsentReportPayload payload)
    {
        var sb = new System.Text.StringBuilder();

        var absentCount = payload.Riders.Count(r => !r.HadShiftButLowHours);
        var lowHoursCount = payload.Riders.Count(r => r.HadShiftButLowHours);

        sb.AppendLine($"تقرير الغياب والساعات المنخفضة — {payload.CompanyName} — {FormatArabicDate(payload.ReportDate)}");
        sb.AppendLine($"لم يعملوا: {absentCount} مندوب | ساعات منخفضة: {lowHoursCount} مندوب | الإجمالي: {payload.Riders.Count}");
        sb.AppendLine(new string('─', 70));

        sb.AppendLine();
        sb.AppendLine($"  {"#",-4} {"الاسم",-25} {"السكن",-20} {"رقم العمل",-15} {"الحالة"}");
        sb.AppendLine($"  {new string('-', 75)}");

        int rank = 1;
        foreach (var r in payload.Riders)
        {
            var status = r.HadShiftButLowHours
                ? $"ساعات منخفضة: {r.WorkingHours:F1} ساعة"
                : "لم يعمل";
            sb.AppendLine($"  {rank,-4} {r.RiderNameAR,-25} {r.HousingName,-20} {r.WorkingId,-15} {status}");
            rank++;
        }

        sb.AppendLine();
        sb.AppendLine("* يُرجى مراجعة هذا التقرير واتخاذ الإجراء المناسب.");
        return sb.ToString();
    }

    // ── HTML body ─────────────────────────────────────────────────────────────
    private static string BuildHtmlBody(AbsentReportPayload payload, string? logoCid)
    {
        var sb = new System.Text.StringBuilder();

        var absentCount = payload.Riders.Count(r => !r.HadShiftButLowHours);
        var lowHoursCount = payload.Riders.Count(r => r.HadShiftButLowHours);

        sb.Append("<html lang=\"ar\" dir=\"rtl\">");
        sb.Append("<head><meta charset=\"UTF-8\"/>");
        sb.Append(EmailCss);
        sb.Append("</head>");
        sb.Append("<body dir=\"rtl\" style=\"direction:rtl;text-align:right;margin:0;padding:0\">");
        sb.Append("<div class=\"wrap\">");

        // ── Top bar ───────────────────────────────────────────────────────────
        sb.Append("<div class=\"topbar\">");
        sb.Append("<table width=\"100%\" cellpadding=\"18\" cellspacing=\"0\" border=\"0\" style=\"direction:rtl\">");
        sb.Append("<tr>");

        // RIGHT cell — main title
        sb.Append("<td align=\"right\" valign=\"middle\" style=\"padding:18px 24px\">");
        sb.Append("<h1 style=\"margin:0 0 5px 0;font-size:19px;color:#fff;font-weight:bold\">⚠️ تقرير الغياب والساعات المنخفضة</h1>");
        sb.Append($"<p style=\"margin:0;font-size:12px;color:#fff;opacity:.85\">");
        sb.Append($"الشركة: {payload.CompanyName} &nbsp;|&nbsp; التاريخ: {FormatArabicDate(payload.ReportDate)}");
        sb.Append("</p>");
        sb.Append("</td>");

        // LEFT cell — logo
        sb.Append("<td width=\"80\" align=\"center\" valign=\"middle\" style=\"padding:12px 16px\">");
        if (logoCid is not null)
        {
            sb.Append($"<img src=\"cid:{logoCid}\" class=\"topbar-logo\" width=\"52\" height=\"52\" alt=\"\" style=\"display:block;border-radius:8px;background:#fff\"/>");
        }
        else
        {
            sb.Append("<div style=\"width:52px;height:52px;background:#fff;border-radius:8px;text-align:center;line-height:52px;font-size:26px\">⚠️</div>");
        }
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // topbar

        // ── Body ──────────────────────────────────────────────────────────────
        sb.Append("<div class=\"body\">");
        sb.Append("<p>السادة المسؤولين،<br/>فيما يلي قائمة المناديب الذين لم يعملوا أو سجّلوا ساعات أقل من 8 ساعات أمس.</p>");

        // ── Summary cards ─────────────────────────────────────────────────────
        sb.Append("<div class=\"summary-box\">");
        sb.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"direction:rtl\">");
        sb.Append("<tr>");

        // Total
        sb.Append("<td align=\"center\" style=\"padding:4px 12px;border-left:1px solid #ffb74d\">");
        sb.Append($"<div class=\"summary-num\" style=\"color:#7b1a1a\">{payload.Riders.Count}</div>");
        sb.Append("<div class=\"summary-label\">إجمالي المتأثرين</div>");
        sb.Append("</td>");

        // Absent
        sb.Append("<td align=\"center\" style=\"padding:4px 12px;border-left:1px solid #ffb74d\">");
        sb.Append($"<div class=\"summary-num\" style=\"color:#c62828\">{absentCount}</div>");
        sb.Append("<div class=\"summary-label\">لم يعملوا</div>");
        sb.Append("</td>");

        // Low hours
        sb.Append("<td align=\"center\" style=\"padding:4px 12px\">");
        sb.Append($"<div class=\"summary-num\" style=\"color:#e65100\">{lowHoursCount}</div>");
        sb.Append("<div class=\"summary-label\">ساعات منخفضة (أقل من 8)</div>");
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // summary-box

        // ── Riders table ──────────────────────────────────────────────────────
        sb.Append("<table style=\"direction:rtl\">");
        sb.Append("<thead><tr>");
        sb.Append("<th class=\"th-num\">#</th>");
        sb.Append("<th>اسم المندوب</th>");
        sb.Append("<th>رقم الإقامة</th>");
        sb.Append("<th>السكن</th>");
        sb.Append("<th>رقم العمل</th>");
        sb.Append("<th class=\"th-num\">الحالة</th>");
        sb.Append("</tr></thead><tbody>");

        int rank = 1;
        foreach (var r in payload.Riders)
        {
            sb.Append("<tr>");

            // rank
            sb.Append($"<td class=\"num\" style=\"color:#999;font-size:11px\">{rank}</td>");

            // name
            sb.Append($"<td style=\"text-align:right\"><strong>{r.RiderNameAR}</strong></td>");

            // iqama
            sb.Append($"<td class=\"num\" style=\"color:#555;font-size:11px\">{r.IqamaNo}</td>");

            // housing
            sb.Append($"<td style=\"text-align:right\">{r.HousingName}</td>");

            // working id
            sb.Append($"<td class=\"num\">{r.WorkingId}</td>");

            // status badge
            if (r.HadShiftButLowHours)
                sb.Append($"<td class=\"num\"><span class=\"badge-low\">⏱ {r.WorkingHours:F1} ساعة</span></td>");
            else
                sb.Append("<td class=\"num\"><span class=\"badge-absent\">✗ لم يعمل</span></td>");

            sb.Append("</tr>");
            rank++;
        }

        sb.Append("</tbody></table>");
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
}