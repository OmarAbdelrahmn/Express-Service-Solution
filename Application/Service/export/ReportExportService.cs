using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.export;


public class ReportExportService : IReportExportService
{
    private readonly Dictionary<string, object> _formatters = new();
    private readonly ExcelGenerator _excelGenerator;
    private readonly PdfGenerator _pdfGenerator;

    public ReportExportService()
    {
        _excelGenerator = new ExcelGenerator();
        _pdfGenerator = new PdfGenerator();
        RegisterFormatters();
    }

    private void RegisterFormatters()
    {
        _formatters["ComprehensiveDashboard"] = new DashboardFormatter();
        // TODO: Add more formatters as you create them
        // _formatters["MonthlyRiderReport"] = new MonthlyReportFormatter();
        // _formatters["TopRidersReport"] = new TopRidersFormatter();
    }

    public async Task<byte[]> ExportToExcelAsync<T>(T reportData, string reportType)
        where T : class
    {
        var formatter = GetFormatter<T>(reportType);
        var format = formatter.FormatForExcel(reportData);
        return await Task.FromResult(_excelGenerator.Generate(format));
    }

    public async Task<byte[]> ExportToPdfAsync<T>(T reportData, string reportType)
        where T : class
    {
        var formatter = GetFormatter<T>(reportType);
        var format = formatter.FormatForPdf(reportData);
        return await Task.FromResult(_pdfGenerator.Generate(format));
    }

    private IReportFormatter<T> GetFormatter<T>(string reportType) where T : class
    {
        if (!_formatters.TryGetValue(reportType, out var formatter))
        {
            throw new NotSupportedException(
                $"Report type '{reportType}' not supported. " +
                $"Available types: {string.Join(", ", _formatters.Keys)}");
        }

        return (IReportFormatter<T>)formatter;
    }
}