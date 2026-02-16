using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Application.Service.export;

public class PdfGenerator
{
    public byte[] Generate(PdfReportFormat format)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(QuestPDF.Helpers.Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Element(c => ComposeHeader(c, format));
                page.Content().Element(c => ComposeContent(c, format));
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, PdfReportFormat format)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(format.Title)
                    .FontSize(20)
                    .Bold()
                    .FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);

                if (!string.IsNullOrEmpty(format.Subtitle))
                {
                    column.Item().Text(format.Subtitle)
                        .FontSize(12)
                        .FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                }

                // Add metadata in header
                if (format.Metadata.Any())
                {
                    column.Item().PaddingTop(5).Row(metaRow =>
                    {
                        foreach (var meta in format.Metadata.Take(3)) // First 3 items
                        {
                            metaRow.RelativeItem().Column(col =>
                            {
                                col.Item().Text(meta.Key)
                                    .FontSize(8)
                                    .SemiBold()
                                    .FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);
                                col.Item().Text(meta.Value)
                                    .FontSize(8)
                                    .FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                            });
                        }
                    });
                }

                column.Item().PaddingTop(10).LineHorizontal(2)
                    .LineColor(QuestPDF.Helpers.Colors.Blue.Medium);
            });
        });
    }

    private void ComposeContent(IContainer container, PdfReportFormat format)
    {
        container.PaddingVertical(10).Column(column =>
        {
            foreach (var section in format.Sections)
            {
                column.Item().Element(c => ComposeSection(c, section));
            }
        });
    }

    private void ComposeSection(IContainer container, PdfSection section)
    {
        container.Column(column =>
        {
            // Section title
            column.Item().PaddingBottom(5).Text(section.Title)
                .FontSize(14)
                .SemiBold()
                .FontColor(QuestPDF.Helpers.Colors.Blue.Darken1);

            column.Item().PaddingBottom(2).LineHorizontal(1)
                .LineColor(QuestPDF.Helpers.Colors.Blue.Lighten2);

            // Section contents
            foreach (var content in section.Contents)
            {
                column.Item().Element(c => RenderContent(c, content));
            }

            column.Item().PaddingBottom(15);
        });
    }

    private void RenderContent(IContainer container, PdfContent content)
    {
        switch (content.Type)
        {
            case PdfContentType.Table:
                ComposeTable(container, (TableData)content.Data);
                break;
            case PdfContentType.KeyValuePairs:
                ComposeKeyValuePairs(container, (Dictionary<string, string>)content.Data);
                break;
            case PdfContentType.Text:
                container.PaddingVertical(3).Text(content.Data.ToString());
                break;
            case PdfContentType.Spacer:
                container.PaddingVertical(10);
                break;
            case PdfContentType.Heading:
                container.Text(content.Data.ToString())
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                break;
        }
    }

    private void ComposeTable(IContainer container, TableData tableData)
    {
        container.PaddingVertical(5).Table(table =>
        {
            // Define columns equally
            table.ColumnsDefinition(columns =>
            {
                for (int i = 0; i < tableData.Headers.Count; i++)
                {
                    columns.RelativeColumn();
                }
            });

            // Header
            table.Header(header =>
            {
                foreach (var headerText in tableData.Headers)
                {
                    header.Cell().Element(HeaderCellStyle).Text(headerText);
                }

                static IContainer HeaderCellStyle(IContainer c) => c
                    .DefaultTextStyle(x => x.SemiBold().FontColor(QuestPDF.Helpers.Colors.White))
                    .PaddingVertical(5)
                    .PaddingHorizontal(3)
                    .Background(QuestPDF.Helpers.Colors.Blue.Medium)
                    .Border(1)
                    .BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1);
            });

            // Rows
            int rowIndex = 0;
            foreach (var row in tableData.Rows)
            {
                var isEvenRow = rowIndex % 2 == 0;

                foreach (var cellValue in row)
                {
                    table.Cell()
                        .Element(c => DataCellStyle(c, isEvenRow))
                        .Text(cellValue?.ToString() ?? "");
                }

                rowIndex++;
            }

            static IContainer DataCellStyle(IContainer c, bool isEven) => c
                .Background(isEven ? QuestPDF.Helpers.Colors.Grey.Lighten3 : QuestPDF.Helpers.Colors.White)
                .Border(1)
                .BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
                .PaddingVertical(4)
                .PaddingHorizontal(3);
        });
    }

    private void ComposeKeyValuePairs(IContainer container, Dictionary<string, string> data)
    {
        container.PaddingVertical(5).Column(column =>
        {
            foreach (var kvp in data)
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem(2)
                        .PaddingRight(10)
                        .Text(kvp.Key)
                        .SemiBold()
                        .FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);

                    row.RelativeItem(3)
                        .Text(kvp.Value)
                        .FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);
                });
            }
        });
    }
}
