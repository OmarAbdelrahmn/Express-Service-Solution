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
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);

                // Arabic requires a font that supports RTL — Scheherazade New is free on Google Fonts
                // drop the .ttf into wwwroot/fonts/ and register it once at app startup:
                // FontManager.RegisterFont(File.OpenRead("wwwroot/fonts/ScheherazadeNew-Regular.ttf"));
                page.DefaultTextStyle(x =>
                    x.FontSize(10)
                     .FontFamily("Scheherazade New")
                     .DirectionFromRightToLeft());

                page.Header().Element(ComposeHeader(payload));
                page.Content().Element(ComposeContent(payload));
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("صفحة ");
                    x.CurrentPageNumber();
                    x.Span(" من ");
                    x.TotalPages();
                });
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
                    .Text($"تقرير وردِيَّات الرُّكَّاب اليومي — {FormatArabicDate(payload.ReportDate)}")
                    .SemiBold()
                    .FontSize(16)
                    .FontColor(Colors.Blue.Darken3);

                col.Item()
                    .AlignRight()
                    .Text($"تاريخ الإنشاء: {DateTime.Now:dd/MM/yyyy HH:mm}  |  " +
                          $"إجمالي الورديات: {payload.GrandTotalShifts}")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken1);

                col.Item()
                    .PaddingTop(4)
                    .LineHorizontal(1)
                    .LineColor(Colors.Blue.Darken3);
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
                        .Padding(6)
                        .AlignRight()
                        .Text($"🏢  الشركة: {company.CompanyName}  —  عدد الورديات: {company.TotalShifts}")
                        .SemiBold()
                        .FontSize(12);

                    foreach (var (housingName, rows) in company.RowsByHousing)
                    {
                        // ── Housing sub-header ───────────────────────────
                        col.Item()
                            .PaddingTop(8)
                            .PaddingHorizontal(10)
                            .Background(Colors.Grey.Lighten3)
                            .Padding(5)
                            .AlignRight()
                            .Text($"🏠  السكن: {housingName}")
                            .SemiBold()
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken3);

                        // ── Section tables (أعلى 5 / أدنى 5) ────────────
                        var sections = rows.GroupBy(r => r.Section);

                        foreach (var section in sections)
                        {
                            var isTop = section.Key == "أعلى 5";
                            var labelClr = isTop ? Colors.Green.Darken2 : Colors.Red.Darken2;
                            var headerBg = isTop ? Colors.Green.Darken2 : Colors.Red.Darken2;

                            col.Item()
                                .PaddingTop(6)
                                .PaddingHorizontal(14)
                                .AlignRight()
                                .Text(section.Key)
                                .Italic()
                                .SemiBold()
                                .FontSize(10)
                                .FontColor(labelClr);

                            col.Item()
                                .PaddingHorizontal(10)
                                .PaddingBottom(6)
                                .Table(table =>
                                {
                                    // Column widths — RTL order in the PDF
                                    // (rightmost = first column visually)
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.RelativeColumn(1.5f);  // ساعات العمل
                                        cols.RelativeColumn(1.5f);  // الطلبات
                                        cols.RelativeColumn(2);     // السكن
                                        cols.RelativeColumn(2);     // رقم الإقامة
                                        cols.RelativeColumn(3);     // الاسم
                                    });

                                    // ── Table header ────────────────────
                                    IContainer HeaderCell(IContainer c) =>
                                        c.Background(headerBg)
                                         .Padding(5)
                                         .AlignCenter();

                                    table.Header(h =>
                                    {
                                        h.Cell().Element(HeaderCell)
                                            .Text("ساعات العمل")
                                            .FontColor(Colors.White).SemiBold();
                                        h.Cell().Element(HeaderCell)
                                            .Text("الطلبات المقبولة")
                                            .FontColor(Colors.White).SemiBold();
                                        h.Cell().Element(HeaderCell)
                                            .Text("السكن")
                                            .FontColor(Colors.White).SemiBold();
                                        h.Cell().Element(HeaderCell)
                                            .Text("رقم الإقامة")
                                            .FontColor(Colors.White).SemiBold();
                                        h.Cell().Element(HeaderCell)
                                            .Text("اسم الراكب")
                                            .FontColor(Colors.White).SemiBold();
                                    });

                                    // ── Data rows ───────────────────────
                                    bool even = false;
                                    foreach (var row in section.OrderByDescending(r => r.AcceptedOrders))
                                    {
                                        var rowBg = even ? Colors.Grey.Lighten4 : Colors.White;
                                        even = !even;

                                        IContainer DataCell(IContainer c) =>
                                            c.Background(rowBg)
                                             .BorderBottom(1)
                                             .BorderColor(Colors.Grey.Lighten2)
                                             .Padding(5)
                                             .AlignCenter();

                                        table.Cell().Element(DataCell)
                                            .Text($"{row.WorkingHours:F1} ساعة");

                                        table.Cell().Element(DataCell)
                                            .Text(row.AcceptedOrders.ToString())
                                            .FontColor(labelClr)
                                            .SemiBold();

                                        table.Cell().Element(DataCell)
                                            .Text(row.HousingName);

                                        table.Cell().Element(DataCell)
                                            .Text(row.IqamaNo.ToString());

                                        table.Cell().Element(DataCell)
                                            .Text(row.RiderNameAR)
                                            .SemiBold();
                                    }
                                });
                        }
                    }

                    // ── Company summary footer bar ───────────────────────
                    col.Item()
                        .PaddingTop(4)
                        .PaddingHorizontal(10)
                        .Background(Colors.Blue.Lighten5)
                        .Padding(5)
                        .AlignRight()
                        .Text($"إجمالي ورديات {company.CompanyName}: {company.TotalShifts} وردية")
                        .FontSize(9)
                        .FontColor(Colors.Blue.Darken3)
                        .Italic();
                }
            });

    // ── Helper: Arabic month names ────────────────────────────────────────────
    private static readonly string[] ArabicMonths =
    [
        "يناير","فبراير","مارس","أبريل","مايو","يونيو",
        "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
    ];

    private static string FormatArabicDate(DateOnly d) =>
        $"{d.Day} {ArabicMonths[d.Month - 1]} {d.Year}";
}