using ClosedXML.Excel;

namespace Application.Service.export;


public class ExcelGenerator
{
    private readonly XLColor _headerColor = XLColor.FromHtml("#4472C4");
    private readonly XLColor _titleColor = XLColor.FromHtml("#203864");
    private readonly XLColor _subtitleColor = XLColor.FromHtml("#5B9BD5");
    private readonly XLColor _alternateRowColor = XLColor.FromHtml("#F2F2F2");

    public byte[] Generate(ExcelReportFormat format)
    {
        using var workbook = new XLWorkbook();

        foreach (var sheetFormat in format.Sheets)
        {
            var worksheet = workbook.Worksheets.Add(sheetFormat.Name);
            int currentRow = 1;

            // Add main title
            if (!string.IsNullOrEmpty(format.Title))
            {
                var titleCell = worksheet.Cell(currentRow, 1);
                titleCell.Value = format.Title;
                titleCell.Style
                    .Font.SetBold()
                    .Font.SetFontSize(16)
                    .Font.SetFontColor(_titleColor)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);

                currentRow += 2;
            }

            // Add metadata
            if (format.Metadata.Any())
            {
                foreach (var meta in format.Metadata)
                {
                    worksheet.Cell(currentRow, 1).Value = meta.Key;
                    worksheet.Cell(currentRow, 1).Style.Font.SetBold();
                    worksheet.Cell(currentRow, 2).Value = meta.Value;
                    currentRow++;
                }
                currentRow++; // Add spacing
            }

            // Add sheet subtitle if exists
            if (!string.IsNullOrEmpty(sheetFormat.SubTitle))
            {
                var subtitleCell = worksheet.Cell(currentRow, 1);
                subtitleCell.Value = sheetFormat.SubTitle;
                subtitleCell.Style
                    .Font.SetBold()
                    .Font.SetFontSize(12)
                    .Font.SetFontColor(_subtitleColor);
                currentRow += 2;
            }

            // Add key-value pairs if exists
            if (sheetFormat.KeyValuePairs != null && sheetFormat.KeyValuePairs.Any())
            {
                currentRow = AddKeyValuePairs(worksheet, sheetFormat.KeyValuePairs, currentRow);
                currentRow += 2;
            }

            // Add tables
            foreach (var table in sheetFormat.Tables)
            {
                currentRow = AddTable(worksheet, table, currentRow);
                currentRow += 2; // Spacing between tables
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Set minimum column width
            foreach (var column in worksheet.ColumnsUsed())
            {
                if (column.Width < 10)
                    column.Width = 10;
                if (column.Width > 50)
                    column.Width = 50;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private int AddKeyValuePairs(IXLWorksheet worksheet, List<ExcelKeyValue> kvPairs, int startRow)
    {
        int currentRow = startRow;

        foreach (var kv in kvPairs)
        {
            if (kv.IsHeader)
            {
                var headerCell = worksheet.Cell(currentRow, 1);
                headerCell.Value = kv.Key;
                headerCell.Style
                    .Font.SetBold()
                    .Font.SetFontSize(11)
                    .Font.SetFontColor(_subtitleColor)
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#E7E6E6"));

                worksheet.Range(currentRow, 1, currentRow, 2).Merge();
                currentRow++;
            }
            else
            {
                worksheet.Cell(currentRow, 1).Value = kv.Key;
                worksheet.Cell(currentRow, 1).Style.Font.SetBold();

                var valueCell = worksheet.Cell(currentRow, 2);
                valueCell.Value = ClosedXML.Excel.XLCellValue.FromObject(kv.Value);

                // Format numbers
                if (kv.Value is decimal or double or float)
                {
                    valueCell.Style.NumberFormat.Format = "#,##0.00";
                }

                currentRow++;
            }
        }

        return currentRow;
    }

    private int AddTable(IXLWorksheet worksheet, ExcelTable table, int startRow)
    {
        int currentRow = startRow;

        // Add table title if exists
        if (!string.IsNullOrEmpty(table.Title))
        {
            var titleCell = worksheet.Cell(currentRow, 1);
            titleCell.Value = table.Title;
            titleCell.Style
                .Font.SetBold()
                .Font.SetFontSize(12)
                .Font.SetFontColor(_subtitleColor);
            currentRow += 2;
        }

        int startDataRow = currentRow;

        // Add headers
        for (int col = 0; col < table.Headers.Count; col++)
        {
            var headerCell = worksheet.Cell(currentRow, col + 1);
            headerCell.Value = table.Headers[col];
            headerCell.Style
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(_headerColor)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }
        currentRow++;

        // Add data rows
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];

            for (int col = 0; col < row.Count; col++)
            {
                var cell = worksheet.Cell(currentRow, col + 1);
                cell.Value = ClosedXML.Excel.XLCellValue.FromObject(row[col]);

                // Alternate row colors
                if (rowIndex % 2 == 0)
                {
                    cell.Style.Fill.SetBackgroundColor(_alternateRowColor);
                }

                // Format numbers
                if (row[col] is decimal or double or float)
                {
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                }
                else if (row[col] is int or long)
                {
                    cell.Style.NumberFormat.Format = "#,##0";
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                }

                // Add borders
                cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            }

            currentRow++;
        }

        // Add totals row if requested
        if (table.AddTotals && table.NumericColumns != null && table.NumericColumns.Any())
        {
            var totalsCell = worksheet.Cell(currentRow, 1);
            totalsCell.Value = "TOTAL";
            totalsCell.Style.Font.SetBold();
            totalsCell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#D9E1F2"));

            foreach (var colIndex in table.NumericColumns)
            {
                var totalCell = worksheet.Cell(currentRow, colIndex + 1);
                var columnLetter = XLHelper.GetColumnLetterFromNumber(colIndex + 1);

                // Use SUM formula
                totalCell.FormulaA1 =
                    $"=SUM({columnLetter}{startDataRow + 1}:{columnLetter}{currentRow - 1})";

                totalCell.Style.Font.SetBold();
                totalCell.Style.NumberFormat.Format = "#,##0.00";
                totalCell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#D9E1F2"));
                totalCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            }

            currentRow++;
        }

        return currentRow;
    }
}