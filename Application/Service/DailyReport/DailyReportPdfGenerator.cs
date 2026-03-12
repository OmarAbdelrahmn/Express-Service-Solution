using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Application.Service.DailyReport;

public static class DailyReportPdfGenerator
{
    public static byte[] Generate(DailyReportPayload payload, byte[]? logoBytes = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);

                // Vertical margins stay 1.5cm — horizontal reduced 30% (1.5 × 0.7 = 1.05)
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginLeft(1.05f, Unit.Centimetre);
                page.MarginRight(1.05f, Unit.Centimetre);

                page.DefaultTextStyle(x =>
                    x.FontSize(10)
                     .FontFamily("Scheherazade New")
                     .DirectionFromRightToLeft());

                page.Header().Element(ComposeHeader(payload, logoBytes));
                page.Content().Element(ComposeContent(payload));
                page.Footer().Element(ComposeFooter());
            });
        }).GeneratePdf();
    }

    // ── Repeating header (all pages) — title + date only, no logo ────────────
    private static Action<IContainer> ComposeHeader(DailyReportPayload payload, byte[]? logoBytes) =>
           header => header
    .PaddingBottom(10)
    .Column(col =>
    {
        // Title row: logo on LEFT, text on RIGHT
        col.Item().Row(row =>
        {
            // RIGHT — title + date
            row.RelativeItem()
                .AlignRight()
                .Column(textCol =>
                {
                    textCol.Item()
                        .AlignRight()
                        .Text($"تقرير وردِيَّات المناديب اليومي — {FormatArabicDate(payload.ReportDate)}")
                        .SemiBold()
                        .FontSize(12)                    // ← reduced from 16
                        .FontColor(Colors.Blue.Darken3);

                    textCol.Item()
                        .AlignRight()
                        .Text($"تاريخ الإنشاء: {DateTime.Now:dd/MM/yyyy HH:mm}  |  " +
                              $"إجمالي الورديات: {payload.GrandTotalShifts}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken3);
                });

            // LEFT — logo
            if (logoBytes is not null)
            {
                row.ConstantItem(70)
                    .AlignLeft()
                    .AlignMiddle()
                    .Height(45)
                    .Image(logoBytes, ImageScaling.FitArea);
            }
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
    private static Action<IContainer> ComposeContent(DailyReportPayload payload) =>
        content => content
            .Column(col =>
            {
                foreach (var company in payload.Companies)
                {
                    // ── Company block header ─────────────────────────────────
                    // RTL Row: secondary info added FIRST (goes LEFT), 
                    //          main label added SECOND (goes RIGHT)
                    col.Item()
                        .PaddingTop(14)
                        .Background(Colors.Blue.Lighten4)
                        .Padding(8)
                        .Row(row =>
                        {
                            // LEFT — secondary
                            row.ConstantItem(180)
                                .AlignLeft()
                                .AlignMiddle()
                                .Text($"إجمالي الورديات: {company.TotalShifts}")
                                .FontSize(10)
                                .FontColor(Colors.Blue.Darken3);

                            // RIGHT — main
                            row.RelativeItem()
                                .AlignRight()
                                .AlignMiddle()
                                .Text($"🏢  الشركة: {company.CompanyName}")
                                .SemiBold()
                                .FontSize(12);
                        });

                    foreach (var (housingName, rows) in company.RowsByHousing)
                    {
                        // ── Housing sub-header ───────────────────────────────
                        col.Item()
                            .PaddingTop(10)
                            .PaddingHorizontal(6)
                            .Background(Colors.Grey.Lighten3)
                            .Padding(6)
                            .Row(row =>
                            {
                                // LEFT — secondary
                                row.ConstantItem(160)
                                    .AlignLeft()
                                    .AlignMiddle()
                                    .Text($"عدد المناديب: {rows.Count}")
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken2);

                                // RIGHT — main
                                row.RelativeItem()
                                    .AlignRight()
                                    .AlignMiddle()
                                    .Text($"🏠  السكن: {housingName}")
                                    .SemiBold()
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken3);
                            });

                        // ── Riders table ─────────────────────────────────────
                        col.Item()
                            .PaddingHorizontal(6)
                            .PaddingBottom(8)
                            .Table(table =>
                            {
                                // RTL column order — left→right in code = right→left visually:
                                // ساعات | طلبات | سكن | إقامة | اسم | #
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(1.5f);  // ساعات العمل  (leftmost visual)
                                    cols.RelativeColumn(1.5f);  // الطلبات
                                    cols.RelativeColumn(2);     // السكن
                                    cols.RelativeColumn(2);     // رقم الإقامة
                                    cols.RelativeColumn(3);     // اسم المندوب
                                    cols.ConstantColumn(30);    // #             (rightmost visual)
                                });

                                IContainer HeaderCell(IContainer c) =>
                                    c.Background(Colors.Blue.Darken3)
                                     .Padding(6)
                                     .AlignCenter();

                                // Header cells — same RTL order as columns
                                table.Header(h =>
                                {
                                    h.Cell().Element(HeaderCell)
                                        .Text("الساعات").FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("الطلبات").FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("السكن").FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("رقم الإقامة").FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("اسم المندوب").FontColor(Colors.White).SemiBold();
                                    h.Cell().Element(HeaderCell)
                                        .Text("#").FontColor(Colors.White).SemiBold();
                                });

                                int rank = 1;
                                foreach (var row in rows)
                                {
                                    var isEven = rank % 2 == 0;
                                    var rowBg = isEven ? Colors.Grey.Lighten4 : Colors.White;

                                    var ordersColor = GetOrdersColor(row.AcceptedOrders);


                                    IContainer DataCell(IContainer c) =>
                                        c.Background(rowBg)
                                         .BorderBottom(1)
                                         .BorderColor(Colors.Grey.Lighten2)
                                         .Padding(5)
                                         .AlignCenter();

                                    // Data cells — same RTL order as columns
                                    table.Cell().Element(DataCell)
                                        .Text($"{row.WorkingHours:F1}ساعة");

                                    table.Cell().Element(DataCell)
                                        .Text(row.AcceptedOrders.ToString())
                                        .FontColor(ordersColor)
                                        .SemiBold();

                                    table.Cell().Element(DataCell)
                                        .Text(row.HousingName);

                                    table.Cell().Element(DataCell)
                                        .Text(row.IqamaNo.ToString());

                                    table.Cell().Element(DataCell)
                                        .Text(row.RiderNameAR)
                                        .SemiBold();

                                    table.Cell().Element(DataCell)
                                        .Text(rank.ToString())
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontSize(9);

                                    rank++;
                                }
                            });

                        // ── Housing summary ──────────────────────────────────
                        col.Item()
                            .PaddingHorizontal(6)
                            .PaddingBottom(4)
                            .Background(Colors.Grey.Lighten2)
                            .Padding(4)
                            .Row(row =>
                            {
                                // LEFT — secondary
                                row.ConstantItem(200)
                                    .AlignLeft()
                                    .Text($"إجمالي الساعات: {rows.Sum(r => r.WorkingHours):F1} ساعة")
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken3)
                                    .Italic();

                                // RIGHT — main
                                row.RelativeItem()
                                    .AlignRight()
                                    .Text($"إجمالي طلبات {housingName}: {rows.Sum(r => r.AcceptedOrders)} طلب")
                                    .FontSize(9)
                                    .FontColor(Colors.Grey.Darken3)
                                    .Italic();
                            });
                    }

                    // ── Company summary footer ───────────────────────────────
                    var companyTotalOrders = company.RowsByHousing
                        .Values.SelectMany(r => r).Sum(r => r.AcceptedOrders);

                    col.Item()
                        .PaddingTop(2)
                        .PaddingHorizontal(6)
                        .PaddingBottom(6)
                        .Background(Colors.Blue.Lighten5)
                        .Padding(6)
                        .Row(row =>
                        {
                            // LEFT — secondary
                            row.ConstantItem(240)
                                .AlignLeft()
                                .Text($"إجمالي الطلبات المقبولة: {companyTotalOrders} طلب")
                                .FontSize(9)
                                .FontColor(Colors.Blue.Darken3)
                                .Italic();

                            // RIGHT — main
                            row.RelativeItem()
                                .AlignRight()
                                .Text($"✔ إجمالي ورديات {company.CompanyName}: {company.TotalShifts} وردية")
                                .FontSize(9)
                                .FontColor(Colors.Blue.Darken3)
                                .Italic();
                        });
                }
            });

    // ── Order color relative to housing group ─────────────────────────────────
    private static string GetOrdersColor(int orders)
    {
        if (orders < 12) return Colors.Red.Darken2;
        if (orders <= 15) return Colors.Orange.Darken2;
        return Colors.Green.Darken2;
    }

    private static readonly string[] ArabicMonths =
    [
        "يناير","فبراير","مارس","أبريل","مايو","يونيو",
        "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
    ];

    private static string FormatArabicDate(DateOnly d) =>
        $"{d.Day} {ArabicMonths[d.Month - 1]} {d.Year}";
}