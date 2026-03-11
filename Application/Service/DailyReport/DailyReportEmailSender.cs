using DocumentFormat.OpenXml.Spreadsheet;
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

    public async Task SendAsync(DailyReportPayload payload, byte[] pdfBytes, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));

        foreach (var email in _settings.RecipientEmails)
            message.To.Add(MailboxAddress.Parse(email));

        message.Subject = $"📊 تقرير الورديات اليومي — {FormatArabicDate(payload.ReportDate)}";

        // Force RTL for email clients that respect it
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

    // ── Plain-text fallback (Arabic) ─────────────────────────────────────────
    private static string BuildTextBody(DailyReportPayload payload)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"تقرير الورديات اليومي — {FormatArabicDate(payload.ReportDate)}");
        sb.AppendLine($"إجمالي الورديات: {payload.GrandTotalShifts}");
        sb.AppendLine(new string('─', 50));

        foreach (var company in payload.Companies)
        {
            sb.AppendLine();
            sb.AppendLine($"الشركة: {company.CompanyName} | عدد الورديات: {company.TotalShifts}");
            sb.AppendLine(new string('═', 40));

            foreach (var (housing, rows) in company.RowsByHousing)
            {
                sb.AppendLine($"  السكن: {housing}");

                foreach (var section in rows.GroupBy(r => r.Section))
                {
                    sb.AppendLine($"    ── {section.Key} ──");
                    sb.AppendLine($"    {"الاسم",-25} {"رقم الإقامة",-15} {"الطلبات",-10} {"ساعات العمل"}");
                    sb.AppendLine($"    {new string('-', 65)}");

                    foreach (var r in section.OrderByDescending(x => x.AcceptedOrders))
                        sb.AppendLine(
                            $"    {r.RiderNameAR,-25} {r.IqamaNo,-15} {r.AcceptedOrders,-10} {r.WorkingHours:F1} ساعة");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("* يُرجى مراجعة ملف PDF المرفق للاطلاع على التقرير الكامل.");
        return sb.ToString();
    }

    private static string BuildHtmlBody(DailyReportPayload payload)
    {
        var sb = new System.Text.StringBuilder();

        const string css = """
        <style>
          body  { font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
                  font-size: 13px; color: #333; background: #f9f9f9;
                  direction: rtl; text-align: right; }
          .wrap { max-width: 900px; margin: 20px auto; background: #fff;
                  border-radius: 8px; overflow: hidden;
                  box-shadow: 0 2px 8px rgba(0,0,0,.1); }
          .topbar { background: #1a3c6e; color: #fff; padding: 18px 24px; }
          .topbar h1 { margin:0; font-size:20px; }
          .topbar p  { margin:4px 0 0; font-size:12px; opacity:.8; }
          .body  { padding: 20px 24px; }
          .company-header { background: #dce8f7; border-right: 5px solid #1a3c6e;
            padding: 10px 14px; margin: 20px 0 8px;
            border-radius: 4px; font-size: 15px; font-weight: bold; }
          .housing-header { background: #f0f0f0; padding: 6px 14px;
            margin: 10px 0 4px; font-size: 13px;
            font-weight: bold; color: #555; border-radius: 3px; }
          .section-label-top    { color:#2e7d32; font-weight:bold; margin: 8px 0 4px; }
          .section-label-bottom { color:#c62828; font-weight:bold; margin: 8px 0 4px; }
          table  { width:100%; border-collapse:collapse; margin-bottom:10px; }
          th     { padding:8px 10px; text-align:center; font-size:12px; }
          td     { padding:7px 10px; text-align:center;
                   border-bottom:1px solid #eee; font-size:12px; }
          .th-top    { background:#2e7d32; color:#fff; }
          .th-bottom { background:#c62828; color:#fff; }
          .orders-top    { color:#2e7d32; font-weight:bold; }
          .orders-bottom { color:#c62828; font-weight:bold; }
          tr:nth-child(even) td { background:#f9f9f9; }
          .company-footer { font-size:11px; color:#1a3c6e; font-style:italic;
            text-align:left; padding: 4px 10px 12px; }
          .footer { background:#f0f0f0; text-align:center;
                    padding:12px; font-size:11px; color:#888; }
        </style>
        """;

        // ── Opening tags (no CSS braces here, safe to interpolate) ──────────────
        sb.Append($"""
        <html lang="ar" dir="rtl">
        <head>
          <meta charset="UTF-8"/>
          <meta name="viewport" content="width=device-width,initial-scale=1"/>
        """);

        sb.Append(css);   // ← plain string, no interpolation, braces are fine

        sb.Append($"""
        </head>
        <body>
        <div class="wrap">
          <div class="topbar">
            <h1>📊 تقرير الورديات اليومي</h1>
            <p>التاريخ: {FormatArabicDate(payload.ReportDate)} &nbsp;|&nbsp;
               إجمالي الورديات: {payload.GrandTotalShifts}</p>
          </div>
          <div class="body">
            <p>السادة المسؤولين،<br/>
               يُرفق بهذا البريد تقرير الورديات اليومي للرُّكَّاب.
               يُرجى الاطلاع على ملف PDF المرفق للحصول على التقرير الكامل.</p>
        """);

        // ── Companies loop (same as before) ─────────────────────────────────────
        foreach (var company in payload.Companies)
        {
            sb.Append($"""
            <div class="company-header">
              🏢 الشركة: {company.CompanyName}
              <span style="font-size:12px;font-weight:normal;color:#555">
                — عدد الورديات: {company.TotalShifts}
              </span>
            </div>
            """);

            foreach (var (housing, rows) in company.RowsByHousing)
            {
                sb.Append($"""<div class="housing-header">🏠 السكن: {housing}</div>""");

                foreach (var section in rows.GroupBy(r => r.Section))
                {
                    var isTop = section.Key == "أعلى 5";
                    var labelClass = isTop ? "section-label-top" : "section-label-bottom";
                    var thClass = isTop ? "th-top" : "th-bottom";
                    var ordClass = isTop ? "orders-top" : "orders-bottom";

                    sb.Append($"""
                    <p class="{labelClass}">— {section.Key} —</p>
                    <table>
                      <thead>
                        <tr>
                          <th class="{thClass}">اسم الراكب</th>
                          <th class="{thClass}">رقم الإقامة</th>
                          <th class="{thClass}">السكن</th>
                          <th class="{thClass}">الطلبات المقبولة</th>
                          <th class="{thClass}">ساعات العمل</th>
                        </tr>
                      </thead>
                      <tbody>
                    """);

                    foreach (var r in section.OrderByDescending(x => x.AcceptedOrders))
                    {
                        sb.Append($"""
                        <tr>
                          <td><strong>{r.RiderNameAR}</strong></td>
                          <td>{r.IqamaNo}</td>
                          <td>{r.HousingName}</td>
                          <td class="{ordClass}">{r.AcceptedOrders}</td>
                          <td>{r.WorkingHours:F1} ساعة</td>
                        </tr>
                        """);
                    }

                    sb.Append("</tbody></table>");
                }
            }

            sb.Append($"""
            <p class="company-footer">
              ✔ إجمالي ورديات {company.CompanyName}: {company.TotalShifts} وردية
            </p>
            """);
        }

        // ── Closing ──────────────────────────────────────────────────────────────
        sb.Append($"""
          </div>
          <div class="footer">
            تم إرسال هذا التقرير تلقائيًا بتاريخ {DateTime.Now:dd/MM/yyyy} الساعة {DateTime.Now:HH:mm}
            &nbsp;|&nbsp; لا تردَّ على هذا البريد
          </div>
        </div>
        </body></html>
        """);

        return sb.ToString();
    }
}