using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace Application.Service.DailyReport;

public interface IMonthlyProgressReportEmailSender
{
    Task SendAsync(
        MonthlyProgressPayload payload,
        byte[]? logoBytes = null,
        CancellationToken ct = default);
}

public class MonthlyProgressReportEmailSender(
    IOptions<DailyReportSettings> options) : IMonthlyProgressReportEmailSender
{
    private readonly DailyReportSettings _settings = options.Value;

    private static readonly string[] ArabicMonths =
    [
        "يناير","فبراير","مارس","أبريل","مايو","يونيو",
        "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
    ];

    private static string FormatArabicDate(DateOnly d) =>
        $"{d.Day} {ArabicMonths[d.Month - 1]} {d.Year}";

    private static string MonthName(DateOnly d) => ArabicMonths[d.Month - 1];

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

        /* ── Outer wrapper: fluid up to 600 px ── */
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
            background: #14532d;
            padding: 18px 20px 14px;
            direction: rtl;
        }
        .topbar h1 {
            color: #fff;
            font-size: 17px;
            font-weight: bold;
            margin-bottom: 5px;
        }
        .topbar p  { color: #bbf7d0; font-size: 12px; line-height: 1.6; }

        /* ── Progress bar ── */
        .progress-wrap {
            background: #f0fdf4;
            border-bottom: 2px solid #16a34a;
            padding: 14px 20px;
            direction: rtl;
        }
        .progress-label {
            font-size: 12px;
            color: #166534;
            margin-bottom: 6px;
            font-weight: bold;
        }
        .progress-track {
            background: #dcfce7;
            border-radius: 20px;
            height: 10px;
            overflow: hidden;
            direction: ltr;   /* bar fills left-to-right regardless of RTL */
        }
        .progress-fill {
            height: 10px;
            border-radius: 20px;
            background: linear-gradient(90deg, #16a34a, #4ade80);
        }
        .progress-nums {
            font-size: 11px;
            color: #555;
            margin-top: 4px;
            direction: rtl;
        }

        /* ── Summary cards row ── */
        .summary {
            padding: 14px 20px;
            background: #fff;
            direction: rtl;
        }
        .summary table { margin: 0; direction: rtl; }
        .stat-num {
            font-size: 26px;
            font-weight: bold;
            line-height: 1.1;
        }
        .stat-label {
            font-size: 11px;
            color: #666;
            margin-top: 3px;
        }
        .stat-red    { color: #dc2626; }
        .stat-orange { color: #d97706; }
        .stat-green  { color: #16a34a; }

        /* ── Section heading ── */
        .section-head {
            background: #14532d;
            color: #fff;
            font-size: 13px;
            font-weight: bold;
            padding: 9px 20px;
            direction: rtl;
        }

        /* ── Riders table: scroll on small screens ── */
        .body { padding: 0 0 16px; }
        .table-scroll { overflow-x: auto; -webkit-overflow-scrolling: touch; }

        table {
            width: 100%;
            border-collapse: collapse;
            direction: rtl;
        }
        th {
            padding: 10px 8px;
            font-size: 12px;
            background: #166534;
            color: #fff;
            text-align: center;
            white-space: nowrap;
        }
        th.th-name { text-align: right; padding-right: 14px; }
        td {
            padding: 10px 8px;
            font-size: 12px;
            border-bottom: 1px solid #e5e7eb;
            text-align: center;
            vertical-align: middle;
        }
        td.td-name { text-align: right; padding-right: 14px; }
        tr:nth-child(even) td { background: #f9fafb; }

        .name-main   { font-weight: bold; font-size: 13px; color: #111; }
        .name-sub    { font-size: 10px; color: #888; margin-top: 2px; }

        /* Order count coloring */
        .orders-good { color: #15803d; font-weight: bold; }
        .orders-warn { color: #b45309; font-weight: bold; }
        .orders-bad  { color: #b91c1c; font-weight: bold; }

        /* Remaining badge */
        .badge-remaining {
            display: inline-block;
            background: #dbeafe;
            color: #1d4ed8;
            font-weight: bold;
            font-size: 13px;
            padding: 3px 10px;
            border-radius: 20px;
            white-space: nowrap;
        }

        /* Shortfall badge */
        .badge-shortfall {
            display: inline-block;
            background: #fee2e2;
            color: #991b1b;
            font-weight: bold;
            font-size: 11px;
            padding: 2px 8px;
            border-radius: 12px;
            white-space: nowrap;
        }

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
        MonthlyProgressPayload payload,
        byte[]? logoBytes = null,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));

        foreach (var email in _settings.RecipientEmails)
            message.To.Add(MailboxAddress.Parse(email));

        message.Subject =
            $"📉 تقرير تأخر الطلبات الشهري — {payload.CompanyName} — {MonthName(payload.ReportDate)} {payload.ReportDate.Year}";
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
    private static string BuildTextBody(MonthlyProgressPayload p)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"تقرير تأخر الطلبات الشهري — {p.CompanyName} — {MonthName(p.ReportDate)} {p.ReportDate.Year}");
        sb.AppendLine($"اليوم: {p.DaysElapsed} من {p.DaysInMonth} | المستهدف المتناسب: {p.ProportionalTarget} طلب | الهدف الشهري: {p.MonthlyTarget} طلب");
        sb.AppendLine(new string('─', 70));
        sb.AppendLine();
        sb.AppendLine($"  {"#",-4} {"الاسم",-25} {"السكن",-20} {"طلباته",-10} {"المستهدف",-10} {"الباقي للهدف",-12} {"التأخر"}");
        sb.AppendLine($"  {new string('-', 85)}");

        int rank = 1;
        foreach (var r in p.Riders)
        {
            sb.AppendLine(
                $"  {rank,-4} {r.RiderNameAR,-25} {r.HousingName,-20} " +
                $"{r.OrdersSoFar,-10} {r.ProportionalTarget,-10} " +
                $"{r.RemainingToFullTarget,-12} {r.Shortfall}");
            rank++;
        }

        sb.AppendLine();
        sb.AppendLine("* يُرجى متابعة هؤلاء المناديب لضمان تحقيق الهدف الشهري.");
        return sb.ToString();
    }

    // ── HTML body ─────────────────────────────────────────────────────────────
    private static string BuildHtmlBody(MonthlyProgressPayload p, string? logoCid)
    {
        var sb = new System.Text.StringBuilder();

        // Calculate overall progress %
        var pctDays = (int)Math.Round((double)p.DaysElapsed / p.DaysInMonth * 100);
        var avgShortfall = p.Riders.Count > 0 ? (int)p.Riders.Average(r => r.Shortfall) : 0;
        var worstShortfall = p.Riders.Count > 0 ? p.Riders.Max(r => r.Shortfall) : 0;

        // ── DOCTYPE / head ────────────────────────────────────────────────────
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
        sb.Append("<h1>📉 تقرير تأخر الطلبات الشهري</h1>");
        sb.Append($"<p>");
        sb.Append($"الشركة: {p.CompanyName} &nbsp;|&nbsp; {MonthName(p.ReportDate)} {p.ReportDate.Year}<br/>");
        sb.Append($"آخر يوم بيانات: {FormatArabicDate(p.ReportDate)} &nbsp;|&nbsp; اليوم {p.DaysElapsed} من {p.DaysInMonth}");
        sb.Append("</p>");
        sb.Append("</td>");

        // Left: logo
        sb.Append("<td width=\"58\" valign=\"middle\" align=\"center\" style=\"padding-right:12px\">");
        if (logoCid is not null)
            sb.Append($"<img src=\"cid:{logoCid}\" width=\"48\" height=\"48\" style=\"display:block;border-radius:8px;background:#fff\" alt=\"\"/>");
        else
            sb.Append("<div style=\"width:48px;height:48px;background:#fff;border-radius:8px;line-height:48px;text-align:center;font-size:24px\">📉</div>");
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // topbar

        // ── Progress bar ──────────────────────────────────────────────────────
        sb.Append("<div class=\"progress-wrap\">");
        sb.Append($"<div class=\"progress-label\">تقدم الشهر — {pctDays}% من الأيام مضت</div>");
        sb.Append("<div class=\"progress-track\">");
        sb.Append($"<div class=\"progress-fill\" style=\"width:{pctDays}%\"></div>");
        sb.Append("</div>");
        sb.Append($"<div class=\"progress-nums\">اليوم {p.DaysElapsed} من {p.DaysInMonth} &nbsp;|&nbsp; المستهدف المتناسب: <strong>{p.ProportionalTarget}</strong> طلب &nbsp;|&nbsp; الهدف الكامل: <strong>{p.MonthlyTarget}</strong> طلب</div>");
        sb.Append("</div>");

        // ── Summary stats ─────────────────────────────────────────────────────
        sb.Append("<div class=\"summary\">");
        sb.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
        sb.Append("<tr>");

        // Behind count
        sb.Append("<td align=\"center\" style=\"padding:4px 8px;border-left:1px solid #e5e7eb\">");
        sb.Append($"<div class=\"stat-num stat-red\">{p.Riders.Count}</div>");
        sb.Append("<div class=\"stat-label\">مندوب متأخر</div>");
        sb.Append("</td>");

        // Avg shortfall
        sb.Append("<td align=\"center\" style=\"padding:4px 8px;border-left:1px solid #e5e7eb\">");
        sb.Append($"<div class=\"stat-num stat-orange\">{avgShortfall}</div>");
        sb.Append("<div class=\"stat-label\">متوسط التأخر</div>");
        sb.Append("</td>");

        // Worst shortfall
        sb.Append("<td align=\"center\" style=\"padding:4px 8px\">");
        sb.Append($"<div class=\"stat-num stat-red\">{worstShortfall}</div>");
        sb.Append("<div class=\"stat-label\">أعلى تأخر</div>");
        sb.Append("</td>");

        sb.Append("</tr>");
        sb.Append("</table>");
        sb.Append("</div>"); // summary

        // ── Riders table ──────────────────────────────────────────────────────
        sb.Append("<div class=\"body\">");
        sb.Append($"<div class=\"section-head\">المناديب المتأخرون عن الهدف ({p.Riders.Count} مندوب)</div>");
        sb.Append("<div class=\"table-scroll\">");
        sb.Append("<table style=\"min-width:480px\">");

        sb.Append("<thead><tr>");
        sb.Append("<th style=\"width:30px\">#</th>");
        sb.Append("<th class=\"th-name\">المندوب / السكن</th>");
        sb.Append("<th>طلباته</th>");
        sb.Append("<th>المستهدف</th>");
        sb.Append("<th>التأخر</th>");
        sb.Append("<th>الباقي للهدف</th>");
        sb.Append("</tr></thead>");

        sb.Append("<tbody>");
        int rank = 1;
        foreach (var r in p.Riders)
        {
            // Color the order count
            var ordersClass = r.OrdersSoFar == 0 ? "orders-bad"
                            : r.OrdersSoFar < r.ProportionalTarget / 2 ? "orders-bad"
                            : "orders-warn";

            sb.Append("<tr>");

            // rank
            sb.Append($"<td style=\"color:#9ca3af;font-size:11px\">{rank}</td>");

            // name + housing + working id stacked
            sb.Append("<td class=\"td-name\">");
            sb.Append($"<div class=\"name-main\">{r.RiderNameAR}</div>");
            sb.Append($"<div class=\"name-sub\">🏠 {r.HousingName} &nbsp;|&nbsp; {r.WorkingId}</div>");
            sb.Append("</td>");

            // orders so far
            sb.Append($"<td><span class=\"{ordersClass}\">{r.OrdersSoFar}</span></td>");

            // proportional target
            sb.Append($"<td style=\"color:#6b7280\">{r.ProportionalTarget}</td>");

            // shortfall
            sb.Append($"<td><span class=\"badge-shortfall\">-{r.Shortfall}</span></td>");

            // remaining to full target
            sb.Append($"<td><span class=\"badge-remaining\">{r.RemainingToFullTarget}</span></td>");

            sb.Append("</tr>");
            rank++;
        }

        sb.Append("</tbody></table>");
        sb.Append("</div>"); // table-scroll

        // Caption
        sb.Append("<div style=\"padding:10px 20px 0;font-size:11px;color:#9ca3af;direction:rtl;text-align:right\">");
        sb.Append("* عمود <strong>الباقي للهدف</strong> = {الهدف الشهري} − طلباته الفعلية حتى الآن.");
        sb.Append("</div>");

        sb.Append("</div>"); // body

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