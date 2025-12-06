//using DocumentFormat.OpenXml.Drawing.Charts;
//using DocumentFormat.OpenXml.Wordprocessing;
//using YourProject.Services.Export.Models;

//namespace Application.Service.export
//{
//    public interface IExportService
//    {
//        // Sync version for small reports
//        ExportResult Export(ExportRequest request);

//        // Async version with cancellation
//        Task<ExportResult> ExportAsync(
//            ExportRequest request,
//            CancellationToken cancellationToken = default);

//        // Stream-based for large data
//        Task<Stream> ExportAsStreamAsync(
//            ExportRequest request,
//            CancellationToken cancellationToken = default);

//        // Batch/async processing for very large reports
//        Task<string> StartExportJobAsync(
//            ExportRequest request,
//            string callbackUrl = null);

//        Task<ExportJobStatus> GetExportJobStatusAsync(string jobId);
//        Task<Stream> GetExportJobResultAsync(string jobId);
//    }
//}

//// Format-specific interfaces
//public interface IPdfExporter
//{
//    Task<byte[]> GeneratePdfAsync<T>(
//        IEnumerable<T> data,
//        PdfExportOptions options,
//        CancellationToken cancellationToken = default);

//    Task<Stream> GeneratePdfStreamAsync<T>(
//        IAsyncEnumerable<T> dataStream,
//        PdfExportOptions options,
//        CancellationToken cancellationToken = default);
//}

//public interface IExcelExporter
//{
//    Task<byte[]> GenerateExcelAsync<T>(
//        IEnumerable<T> data,
//        ExcelExportOptions options,
//        CancellationToken cancellationToken = default);

//    Task<Stream> GenerateExcelStreamAsync<T>(
//        IAsyncEnumerable<T> dataStream,
//        ExcelExportOptions options,
//        CancellationToken cancellationToken = default);
//}

//// ExportRequest.cs
//namespace YourProject.Services.Export.Models
//{
//    public class ExportRequest
//    {
//        public string ReportId { get; set; }  // Maps to one of your 26 endpoints
//        public ExportFormat Format { get; set; } = ExportFormat.Excel;
//        public Dictionary<string, object> Filters { get; set; } = new();
//        public ExportOptions Options { get; set; } = new();
//        public string Language { get; set; } = "ar"; // Arabic by default
//        public string TimeZone { get; set; } = "Arab Standard Time";

//        // For very large exports
//        public bool UseAsyncProcessing { get; set; }
//        public int? MaxRows { get; set; }
//        public bool CompressOutput { get; set; } = true;
//    }

//    public enum ExportFormat
//    {
//        Excel,
//        Pdf,
//        Csv,
//        Html
//    }

//    public class ExportOptions
//    {
//        public PageSize PaperSize { get; set; } = PageSize.A4;
//        public Orientation Orientation { get; set; } = Orientation.Landscape;
//        public bool IncludeCharts { get; set; }
//        public bool IncludeSummary { get; set; } = true;

//        // Arabic-specific
//        public bool RightToLeft { get; set; } = true;
//        public string ArabicFont { get; set; } = "Arial";
//        public bool EmbedFonts { get; set; } = true;

//        // Excel-specific
//        public bool FreezeHeader { get; set; } = true;
//        public bool AutoFilter { get; set; } = true;
//        public bool AutoFitColumns { get; set; } = true;

//        // PDF-specific
//        public bool AddPageNumbers { get; set; } = true;
//        public string Watermark { get; set; }
//        public bool EncryptPdf { get; set; }
//    }

//    public class ExportResult
//    {
//        public byte[] Data { get; set; }
//        public string FileName { get; set; }
//        public string ContentType { get; set; }
//        public long FileSize { get; set; }
//        public TimeSpan ProcessingTime { get; set; }
//        public bool IsSuccess { get; set; }
//        public string ErrorMessage { get; set; }
//    }

//    public class ExportJobStatus
//    {
//        public string JobId { get; set; }
//        public ExportJobState State { get; set; }
//        public int ProgressPercentage { get; set; }
//        public string DownloadUrl { get; set; }
//        public DateTime? EstimatedCompletion { get; set; }
//        public string Error { get; set; }
//    }

//    public enum ExportJobState
//    {
//        Pending,
//        Processing,
//        Completed,
//        Failed,
//        Cancelled
//    }
//}