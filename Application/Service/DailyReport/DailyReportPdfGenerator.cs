using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Application.Service.DailyReport;

public static class DailyReportPdfGenerator
{
    public static byte[] Generate(DailyReportPayload payload)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x =>
                    x.FontSize(13)
                     .FontFamily("Scheherazade New")
                     .DirectionFromRightToLeft());

                page.Header().Element(ComposeHeader(payload));
                page.Content().Element(ComposeContent(payload));
                page.Footer().Element(ComposeFooter());
            });
        }).GeneratePdf();
    }

    // ── Header ───────────────────────────────────────────────────────────────
    private static Action<IContainer> ComposeHeader(DailyReportPayload payload) =>
        header => header
            .PaddingBottom(10)
            .Column(col =>
            {
                col.Item()
                    .AlignRight()
                    .Text($"تقرير وردِيَّات المناديب اليومي — {FormatArabicDate(payload.ReportDate)}")
                    .SemiBold()
                    .FontSize(16)
                    .FontColor(Colors.Blue.Darken3);

                col.Item()
                    .AlignRight()
                    .Text($"تاريخ الإنشاء: {DateTime.Now:dd/MM/yyyy HH:mm}  |  " +
                          $"إجمالي الورديات: {payload.GrandTotalShifts}")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken3);

                col.Item()
                    .PaddingTop(6)
                    .LineHorizontal(1)
                    .LineColor(Colors.Blue.Darken3);
            });

    // ── Footer ───────────────────────────────────────────────────────────────
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

    // ── Content ──────────────────────────────────────────────────────────────
    private static Action<IContainer> ComposeContent(DailyReportPayload payload) =>
        content => content
            .Column(col =>
            {
                foreach (var company in payload.Companies)
                {
                    // ── Company block header ─────────────────────────────
                    col.Item()
                        .PaddingTop(14)
                        .Background(Colors.Blue.Lighten4)
                        .Padding(8)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .AlignRight()
                                .Text($"🏢  الشركة: {company.CompanyName}")
                                .SemiBold()
                                .FontSize(12);

                            row.ConstantItem(200)
                                .AlignLeft()
                                .Text($"إجمالي الورديات: {company.TotalShifts}")
                                .FontSize(10)
                                .FontColor(Colors.Blue.Darken3);
                        });

                    foreach (var (housingName, rows) in company.RowsByHousing)
                    {
                        // ── Housing sub-header ───────────────────────────
                        col.Item()
                            .PaddingTop(10)
                            .PaddingHorizontal(6)
                            .Background(Colors.Grey.Lighten3)
                            .Padding(6)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .AlignRight()
                                    .Text($"🏠  السكن: {housingName}")
                                    .SemiBold()
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken3);

                                row.ConstantItem(180)
                                    .AlignLeft()
                                    .Text($"عدد المناديب: {rows.Count}")
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken2);
                            });

                        // ── Riders table ─────────────────────────────────
                        col.Item()
                            .PaddingHorizontal(6)
                            .PaddingBottom(8)
                            .Table(table =>
                            {
                                // Columns — RTL: rightmost column defined first
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(30);    // # (rank)
                                    cols.RelativeColumn(3);     // اسم المندوب
                                    cols.RelativeColumn(2);     // رقم الإقامة
                                    cols.RelativeColumn(2);     // السكن
                                    cols.RelativeColumn(1.5f);  // الطلبات
                                    cols.RelativeColumn(1.5f);  // ساعات العمل
                                });

                                // ── Table header ────────────────────────
                                IContainer HeaderCell(IContainer c) =>
                                    c.Background(Colors.Blue.Darken3)
                                     .Padding(6)
                                     .AlignCenter();

                                table.Header(h =>
                                {
                                    h.Cell().Element(HeaderCell)
                                        .Text("#")
                                        .FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("اسم المندوب")
                                        .FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("رقم الإقامة")
                                        .FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("السكن")
                                        .FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("الطلبات")
                                        .FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("الساعات")
                                        .FontColor(Colors.White).SemiBold();
                                });

                                // ── Data rows ───────────────────────────
                                // Already ordered desc by AcceptedOrders from BuildPayload
                                int rank = 1;
                                foreach (var row in rows)
                                {
                                    var isEven = rank % 2 == 0;
                                    var rowBg = isEven ? Colors.Grey.Lighten4 : Colors.White;

                                    // Color-code orders: top third green, bottom third red
                                    var ordersColor = GetOrdersColor(
                                        row.AcceptedOrders,
                                        rows.Max(r => r.AcceptedOrders),
                                        rows.Min(r => r.AcceptedOrders));

                                    IContainer DataCell(IContainer c) =>
                                        c.Background(rowBg)
                                         .BorderBottom(1)
                                         .BorderColor(Colors.Grey.Lighten2)
                                         .Padding(5)
                                         .AlignCenter();

                                    table.Cell().Element(DataCell)
                                        .Text(rank.ToString())
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontSize(9);

                                    table.Cell().Element(DataCell)
                                        .Text(row.RiderNameAR)
                                        .SemiBold();

                                    table.Cell().Element(DataCell)
                                        .Text(row.IqamaNo.ToString());

                                    table.Cell().Element(DataCell)
                                        .Text(row.HousingName);

                                    table.Cell().Element(DataCell)
                                        .Text(row.AcceptedOrders.ToString())
                                        .FontColor(ordersColor)
                                        .SemiBold();

                                    table.Cell().Element(DataCell)
                                        .Text($"ساعة{row.WorkingHours:F1}");

                                    rank++;
                                }
                            });

                        // ── Housing summary row ──────────────────────────
                        col.Item()
                            .PaddingHorizontal(6)
                            .PaddingBottom(4)
                            .Background(Colors.Grey.Lighten2)
                            .Padding(4)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .AlignRight()
                                    .Text($"إجمالي طلبات {housingName}: " +
                                          $"{rows.Sum(r => r.AcceptedOrders)} طلب")
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken3)
                                    .Italic();

                                row.ConstantItem(200)
                                    .AlignLeft()
                                    .Text($"إجمالي الساعات: " +
                                          $"{rows.Sum(r => r.WorkingHours):F1} ساعة")
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken3)
                                    .Italic();
                            });
                    }

                    // ── Company summary footer ───────────────────────────
                    col.Item()
                        .PaddingTop(2)
                        .PaddingHorizontal(6)
                        .PaddingBottom(6)
                        .Background(Colors.Blue.Lighten5)
                        .Padding(6)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .AlignRight()
                                .Text($"✔ إجمالي ورديات {company.CompanyName}: " +
                                      $"{company.TotalShifts} وردية")
                                .FontSize(9)
                                .FontColor(Colors.Blue.Darken3)
                                .Italic();

                            var totalOrders = company.RowsByHousing
                                .Values
                                .SelectMany(r => r)
                                .Sum(r => r.AcceptedOrders);

                            row.ConstantItem(240)
                                .AlignLeft()
                                .Text($"إجمالي الطلبات المقبولة: {totalOrders} طلب")
                                .FontSize(9)
                                .FontColor(Colors.Blue.Darken3)
                                .Italic();
                        });
                }
            });

    // ── Color-code orders relative to the housing group ──────────────────────
    private static string GetOrdersColor(int orders, int max, int min)
    {
        if (max == min) return Colors.Black;

        var range = max - min;
        var topThird = min + (range * 2 / 3.0);
        var bottomThird = min + (range / 3.0);

        if (orders >= topThird) return Colors.Green.Darken2;
        if (orders <= bottomThird) return Colors.Red.Darken2;
        return Colors.Orange.Darken2;
    }

    // ── Arabic date helper ────────────────────────────────────────────────────
    private static readonly string[] ArabicMonths =
    [
        "يناير","فبراير","مارس","أبريل","مايو","يونيو",
        "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
    ];

    private static string FormatArabicDate(DateOnly d) =>
        $"{d.Day} {ArabicMonths[d.Month - 1]} {d.Year}";
}