//using Application.Service.export;
//using System.Globalization;
//using System.Text;
//using System.Threading;

//public class ArabicExportService : IExportService
//{
//    private readonly IPdfGenerator _pdfGenerator;
//    private readonly IExcelGenerator _excelGenerator;

//    public async Task<ExportResult> Export(
//        ExportRequest request,
//        CancellationToken cancellationToken)
//    {
//        // Strategy pattern for different report types
//        var strategy = _strategyFactory.GetStrategy(request.ReportType);

//        // Stream data directly to avoid memory issues
//        await using var dataStream = await strategy.GetDataStream(request.Filters);

//        // Process based on format
//        return request.Format switch
//        {
//            ExportFormat.Pdf => await ExportToPdf(dataStream, request),
//            ExportFormat.Excel => await ExportToExcel(dataStream, request),
//            _ => throw new NotSupportedException()
//        };
//    }

//    private async Task<ExportResult> ExportToPdf(
//        Stream dataStream,
//        ExportRequest request)
//    {
//        var options = new PdfGenerationOptions
//        {
//            // Arabic-specific settings
//            RightToLeft = true,
//            DefaultFont = new ArabicFont("Arial", embedFont: true),
//            Encoding = Encoding.UTF8,

//            // Performance for large data
//            StreamOutput = true,
//            BufferSize = 81920,
//            EnableCompression = true
//        };

//        return await _pdfGenerator.GenerateAsync(dataStream, options);
//    }
//}

//public class StreamingExcelGenerator
//{
//    public async Task GenerateAsync(
//        Stream outputStream,
//        IAsyncEnumerable<ReportRow> data,
//        ExcelOptions options)
//    {
//        using var package = new ExcelPackage(outputStream);
//        var worksheet = package.Workbook.Worksheets.Add("التقرير");

//        // RTL for entire sheet
//        worksheet.View.RightToLeft = options.RightToLeft;

//        // Write headers in Arabic
//        WriteArabicHeaders(worksheet, options.ArabicHeaders);

//        // Stream data in chunks
//        int row = 2;
//        await foreach (var chunk in data.WithCancellation(cancellationToken)
//            .Buffer(5000)) // Process 5K rows at a time
//        {
//            foreach (var item in chunk)
//            {
//                WriteRow(worksheet, row++, item, options);

//                // Flush every 1000 rows to manage memory
//                if (row % 1000 == 0)
//                {
//                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
//                    await Task.Yield(); // Prevent UI freeze in async context
//                }
//            }
//        }

//        await package.SaveAsync(cancellationToken);
//    }
//}

//// MUST HAVE in backend:
//public class ArabicExportSettings
//{
//    public bool RightToLeft { get; set; } = true;
//    public string FontFamily { get; set; } = "Arial";
//    public bool EmbedFont { get; set; } = true;
//    public Encoding TextEncoding { get; set; } = Encoding.UTF8;
//    public CultureInfo Culture { get; set; } = new CultureInfo("ar-SA");

//    // Date/Number formatting for Arabic
//    public string DateFormat { get; set; } = "dd/MM/yyyy";
//    public NumberFormatInfo NumberFormat { get; set; } =
//        CultureInfo.GetCultureInfo("ar-SA").NumberFormat;
//}