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

    // ── Mobile-first CSS ──────────────────────────────────────────────────────
    private const string EmailCss = """
        <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }

        body {
            font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
            font-size: 14px;
            color: #222;
            background: #f3f4f6;
            direction: rtl;
            text-align: right;
        }

        /* ── Wrapper: fluid, max 600 px ── */
        .wrap {
            max-width: 600px;
            width: 100%;
            margin: 12px auto;
            background: #fff;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 2px 10px rgba(0,0,0,.08);
            direction: rtl;
        }

        /* ── Top bar ── */
        .topbar {
            background: #7b1a1a;
            padding: 16px 18px 12px;
            direction: rtl;
        }
        .topbar h1 {
            color: #fff;
            font-size: 16px;
            font-weight: bold;
            margin-bottom: 4px;
        }
        .topbar p  { color: #fca5a5; font-size: 12px; line-height: 1.6; }

        /* ── Summary box ── */
        .summary {
            background: #fff7f7;
            border-bottom: 2px solid #dc2626;
            padding: 14px 18px;
            direction: rtl;
        }
        .summary table { margin: 0; direction: rtl; }

        .stat-num   { font-size: 28px; font-weight: bold; line-height: 1.1; }
        .stat-label { font-size: 11px; color: #6b7280; margin-top: 3px; }
        .num-total  { color: #7b1a1a; }
        .num-absent { color: #b91c1c; }
        .num-low    { color: #d97706; }

        /* ── Section heading ── */
        .section-head {
            background: #7b1a1a;
            color: #fff;
            font-size: 13px;
            font-weight: bold;
            padding: 9px 18px;
            direction: rtl;
        }

        /* ── Rider cards — one per row, no wide table ── */
        .cards { padding: 8px 12px 16px; }

        .rider-card {
            border: 1px solid #e5e7eb;
            border-radius: 8px;
            margin-bottom: 8px;
            overflow: hidden;
            direction: rtl;
        }
        .card-header {
            padding: 9px 12px;
            display: block;
            direction: rtl;
        }
        .card-header-absent { background: #fff1f2; border-bottom: 1px solid #fecaca; }
        .card-header-low    { background: #fffbeb; border-bottom: 1px solid #fde68a; }

        .card-rank   { font-size: 11px; color: #9ca3af; float: left; }
        .card-name   { font-size: 14px; font-weight: bold; color: #111; }
        .card-body   { padding: 8px 12px 10px; display: block; direction: rtl; }

        .card-meta {
            font-size: 12px;
            color: #555;
            line-height: 1.8;
            direction: rtl;
        }
        .card-meta span { white-space: nowrap; }

        /* Status badges */
        .badge-absent {
            display: inline-block;
            background: #fee2e2;
            color: #991b1b;
            font-weight: bold;
            font-size: 12px;
            padding: 3px 10px;
            border-radius: 20px;
            white-space: nowrap;
        }
        .badge-low {
            display: inline-block;
            background: #fef3c7;
            color: #92400e;
            font-weight: bold;
            font-size: 12px;
            padding: 3px 10px;
            border-radius: 20px;
            white-space: nowrap;
        }

        /* clear floats */
        .cf::after { content:""; display:table; clear:both; }

        /* ── Footer ── */
        .footer {
            background: #f1f5f9;
            text-align: center;
            padding: 12px 16px;
            font-size: 11px;
            color: #94a3b8;
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

        message.Subject =
            $"⚠️ تقرير الغياب والساعات المنخفضة — {payload.CompanyName} — {FormatArabicDate(payload.ReportDate)}";
        message.Headers.Add("Content-Language", "ar");

        var body = new BodyBuilder();

        // ── Embed logo ────────────────────────────────────────────────────────
        string? logoCid = null;
        if (logoBytes is not null)
        {
            var res = body.LinkedResources.Add(
                "company-logo.png", logoBytes, new ContentType("image", "png"));
            res.ContentId = MimeUtils.GenerateMessageId();
            logoCid = res.ContentId;
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
        sb.AppendLine($"لم يعملوا: {absentCount} | ساعات منخفضة: {lowHoursCount} | الإجمالي: {payload.Riders.Count}");
        sb.AppendLine(new string('─', 70));

        int rank = 1;
        foreach (var r in payload.Riders)
        {
            var status = r.HadShiftButLowHours
                ? $"ساعات منخفضة: {r.WorkingHours:F1}ساعة"
                : "لم يعمل";
            sb.AppendLine($"{rank}. {r.RiderNameAR} | {r.HousingName} | {r.WorkingId} | {status}");
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

        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"ar\" dir=\"rtl\">");
        sb.Append("<head>");
        sb.Append("<meta charset=\"UTF-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"/>");
        sb.Append(EmailCss);
        sb.Append("</head>");
        sb.Append("<body style=\"direction:rtl;margin:0;padding:8px\">");
        sb.Append("<div class=\"wrap\">");

        // ── Top bar ───────────────────────────────────────────────────────────
        sb.Append("<div class=\"topbar\">");
        sb.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
        sb.Append("<tr>");

        // Right: title
        sb.Append("<td valign=\"middle\">");
        sb.Append("<h1>⚠️ تقرير الغياب والساعات المنخفضة</h1>");
        sb.Append($"<p>الشركة: {payload.CompanyName}<br/>التاريخ: {FormatArabicDate(payload.ReportDate)}</p>");
        sb.Append("</td>");

        // Left: logo
        sb.Append("<td width=\"58\" valign=\"middle\" align=\"center\" style=\"padding-right:12px\">");
        if (logoCid is not null)
            sb.Append($"<img src=\"cid:{logoCid}\" width=\"48\" height=\"48\" style=\"display:block;border-radius:8px;background:#fff\" alt=\"\"/>");
        else
            sb.Append("<div style=\"width:48px;height:48px;background:#fff3f3;border-radius:8px;line-height:48px;text-align:center;font-size:24px\">⚠️</div>");
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // topbar

        // ── Summary ───────────────────────────────────────────────────────────
        sb.Append("<div class=\"summary\">");
        sb.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
        sb.Append("<tr>");

        sb.Append("<td align=\"center\" style=\"padding:4px 8px;border-left:1px solid #fecaca\">");
        sb.Append($"<div class=\"stat-num num-total\">{payload.Riders.Count}</div>");
        sb.Append("<div class=\"stat-label\">إجمالي المتأثرين</div>");
        sb.Append("</td>");

        sb.Append("<td align=\"center\" style=\"padding:4px 8px;border-left:1px solid #fecaca\">");
        sb.Append($"<div class=\"stat-num num-absent\">{absentCount}</div>");
        sb.Append("<div class=\"stat-label\">لم يعملوا</div>");
        sb.Append("</td>");

        sb.Append("<td align=\"center\" style=\"padding:4px 8px\">");
        sb.Append($"<div class=\"stat-num num-low\">{lowHoursCount}</div>");
        sb.Append("<div class=\"stat-label\">ساعات منخفضة</div>");
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // summary

        // ── Riders as cards ───────────────────────────────────────────────────
        sb.Append($"<div class=\"section-head\">قائمة المناديب ({payload.Riders.Count})</div>");
        sb.Append("<div class=\"cards\">");

        int rank = 1;
        foreach (var r in payload.Riders)
        {
            var isLow = r.HadShiftButLowHours;
            var headerClass = isLow ? "card-header card-header-low" : "card-header card-header-absent";

            sb.Append("<div class=\"rider-card\">");

            // card header: rank (left) + name (right)
            sb.Append($"<div class=\"{headerClass} cf\">");
            sb.Append($"<span class=\"card-rank\">{rank}</span>");
            sb.Append($"<span class=\"card-name\">{r.RiderNameAR}</span>");
            sb.Append("</div>");

            // card body: meta info + badge
            sb.Append("<div class=\"card-body\">");
            sb.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
            sb.Append("<tr>");

            // meta info — right cell
            sb.Append("<td valign=\"top\" style=\"direction:rtl\">");
            sb.Append("<div class=\"card-meta\">");
            sb.Append($"<span>🏠 {r.HousingName}</span><br/>");
            sb.Append($"<span>🪪 {r.IqamaNo} &nbsp;|&nbsp; 🔖 {r.WorkingId}</span>");
            sb.Append("</div>");
            sb.Append("</td>");

            // badge — left cell
            sb.Append("<td align=\"left\" valign=\"middle\" style=\"padding-right:8px;white-space:nowrap\">");
            if (isLow)
                sb.Append($"<span class=\"badge-low\">⏱ {r.WorkingHours:F1} ساعة</span>");
            else
                sb.Append("<span class=\"badge-absent\">✗ لم يعمل</span>");
            sb.Append("</td>");

            sb.Append("</tr>");
            sb.Append("</table>");
            sb.Append("</div>"); // card-body

            sb.Append("</div>"); // rider-card
            rank++;
        }

        sb.Append("</div>"); // cards

        // ── Footer ────────────────────────────────────────────────────────────
        sb.Append("<div class=\"footer\">");
        sb.Append($"أُرسل تلقائيًا بتاريخ {DateTime.Now:dd/MM/yyyy} الساعة {DateTime.Now:HH:mm}");
        sb.Append(" &nbsp;|&nbsp; لا تردَّ على هذا البريد");
        sb.Append("</div>");

        sb.Append("</div>"); // wrap
        sb.Append("</body></html>");

        return sb.ToString();
    }
}