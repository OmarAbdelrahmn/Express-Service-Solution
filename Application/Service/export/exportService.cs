using Application.Service.Reports;

namespace Application.Service.export;


public interface IReportExportService
{
    Task<byte[]> ExportToExcelAsync<T>(T reportData, string reportType) where T : class;
    Task<byte[]> ExportToPdfAsync<T>(T reportData, string reportType) where T : class;
}

public interface IReportFormatter<T> where T : class
{
    ExcelReportFormat FormatForExcel(T data);
    PdfReportFormat FormatForPdf(T data);
}

// Excel formatting models
public class ExcelReportFormat
{
    public string Title { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<ExcelSheet> Sheets { get; set; } = new();
}

public class ExcelSheet
{
    public string Name { get; set; } = string.Empty;
    public string? SubTitle { get; set; }
    public List<ExcelTable> Tables { get; set; } = new();
    public List<ExcelKeyValue>? KeyValuePairs { get; set; }
}

public class ExcelTable
{
    public string Title { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<object>> Rows { get; set; } = new();
    public bool AddTotals { get; set; } = false;
    public List<int>? NumericColumns { get; set; } // Columns to sum in totals
}

public class ExcelKeyValue
{
    public string Key { get; set; } = string.Empty;
    public object Value { get; set; } = string.Empty;
    public bool IsHeader { get; set; } = false;
}

// PDF formatting models
public class PdfReportFormat
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<PdfSection> Sections { get; set; } = new();
}

public class PdfSection
{
    public string Title { get; set; } = string.Empty;
    public List<PdfContent> Contents { get; set; } = new();
}

public class PdfContent
{
    public PdfContentType Type { get; set; }
    public object Data { get; set; } = new();
}

public enum PdfContentType
{
    Text,
    Table,
    KeyValuePairs,
    List,
    Heading,
    Spacer
}

public class TableData
{
    public List<string> Headers { get; set; } = new();
    public List<List<object>> Rows { get; set; } = new();
    public string? Title { get; set; }
}



// ============= PDF GENERATOR WITH QUESTPDF =============

// ============= FORMATTERS =============

// Example: ComprehensiveDashboard Formatter
public class DashboardFormatter : IReportFormatter<ComprehensiveDashboard>
{
    public ExcelReportFormat FormatForExcel(ComprehensiveDashboard data)
    {
        return new ExcelReportFormat
        {
            Title = "📊 Comprehensive Dashboard Report",
            Metadata = new Dictionary<string, string>
            {
                ["Generated At"] = data.GeneratedAt.ToString("yyyy-MM-dd HH:mm"),
                ["Period"] = $"{data.PeriodStart:yyyy-MM-dd} to {data.PeriodEnd:yyyy-MM-dd}",
                ["Total Companies"] = data.Companies.TotalCompanies.ToString(),
                ["Total Riders"] = data.Riders.TotalRiders.ToString()
            },
            Sheets = new List<ExcelSheet>
            {
                // Overview Sheet
                new ExcelSheet
                {
                    Name = "Overview",
                    SubTitle = "Summary Statistics",
                    KeyValuePairs = new List<ExcelKeyValue>
                    {
                        new() { Key = "📦 Orders Statistics", Value = "", IsHeader = true },
                        new() { Key = "Total Orders", Value = data.Orders.TotalOrders },
                        new() { Key = "Accepted Orders", Value = data.Orders.TotalAcceptedOrders },
                        new() { Key = "Rejected Orders", Value = data.Orders.TotalRejectedOrders },
                        new() { Key = "Acceptance Rate", Value = $"{data.Orders.AcceptanceRate:F2}%" },
                        new() { Key = "Stacked Deliveries", Value = data.Orders.TotalStackedDeliveries },

                        new() { Key = "👥 Riders Statistics", Value = "", IsHeader = true },
                        new() { Key = "Total Riders", Value = data.Riders.TotalRiders },
                        new() { Key = "Active Riders", Value = data.Riders.ActiveRiders },
                        new() { Key = "Avg Shifts/Rider", Value = $"{data.Riders.AverageShiftsPerRider:F1}" },

                        new() { Key = "📈 Performance", Value = "", IsHeader = true },
                        new() { Key = "Overall Score", Value = $"{data.Performance.OverallPerformanceScore:F2}%" },
                        new() { Key = "Avg Completion Rate", Value = $"{data.Performance.AverageCompletionRate:F2}%" }
                    }
                },
                
                // Companies Sheet
                new ExcelSheet
                {
                    Name = "Companies",
                    SubTitle = "Company Performance Details",
                    Tables = new List<ExcelTable>
                    {
                        new ExcelTable
                        {
                            Title = "Company Performance",
                            Headers = new List<string>
                            {
                                "Company Name", "Target/Day", "Total Shifts",
                                "Active Riders", "Accepted Orders", "Rejected Orders",
                                "Completed", "Failed", "Performance %", "Working Hrs"
                            },
                            Rows = data.Companies.CompanyDetails.Select(c => new List<object>
                            {
                                c.CompanyName, c.DailyOrderTarget, c.TotalShifts,
                                c.ActiveRiders, c.TotalAcceptedOrders, c.TotalRejectedOrders,
                                c.CompletedShifts, c.FailedShifts, c.PerformanceScore,
                                c.TotalWorkingHours
                            }).ToList(),
                            AddTotals = true,
                            NumericColumns = new List<int> { 2, 3, 4, 5, 6, 7, 9 }
                        }
                    }
                },
                
                // Top Performers Sheet
                new ExcelSheet
                {
                    Name = "Top Performers",
                    Tables = new List<ExcelTable>
                    {
                        new ExcelTable
                        {
                            Title = "Top 10 Riders",
                            Headers = new List<string>
                            {
                                "Rank", "Rider Name", "Working ID", "Total Orders",
                                "Performance %", "Completion %"
                            },
                            Rows = data.Performance.TopPerformers.Select((p, index) =>
                                new List<object>
                                {
                                    index + 1, p.RiderName, p.WorkingId, p.TotalOrders,
                                    p.PerformanceScore, p.CompletionRate
                                }).ToList()
                        }
                    }
                },
                
                // Housing Sheet
                new ExcelSheet
                {
                    Name = "Housing",
                    Tables = new List<ExcelTable>
                    {
                        new ExcelTable
                        {
                            Title = "Housing Performance",
                            Headers = new List<string>
                            {
                                "Housing Name", "Total Riders", "Total Shifts",
                                "Total Orders", "Accepted", "Completion %"
                            },
                            Rows = data.Housing.HousingDetails.Select(h => new List<object>
                            {
                                h.HousingName, h.TotalRiders, h.TotalShifts,
                                h.TotalOrders, h.AcceptedOrders, h.CompletionRate
                            }).ToList()
                        }
                    }
                }
            }
        };
    }

    public PdfReportFormat FormatForPdf(ComprehensiveDashboard data)
    {
        return new PdfReportFormat
        {
            Title = "Comprehensive Dashboard Report",
            Subtitle = $"Period: {data.PeriodStart:MMM dd, yyyy} - {data.PeriodEnd:MMM dd, yyyy}",
            Metadata = new Dictionary<string, string>
            {
                ["Generated"] = data.GeneratedAt.ToString("yyyy-MM-dd HH:mm"),
                ["Companies"] = data.Companies.TotalCompanies.ToString(),
                ["Active Riders"] = data.Riders.ActiveRiders.ToString()
            },
            Sections = new List<PdfSection>
            {
                new PdfSection
                {
                    Title = "Executive Summary",
                    Contents = new List<PdfContent>
                    {
                        new()
                        {
                            Type = PdfContentType.KeyValuePairs,
                            Data = new Dictionary<string, string>
                            {
                                ["Total Orders"] = data.Orders.TotalOrders.ToString("N0"),
                                ["Acceptance Rate"] = $"{data.Orders.AcceptanceRate:F1}%",
                                ["Overall Performance"] = $"{data.Performance.OverallPerformanceScore:F1}%",
                                ["Active Riders"] = data.Riders.ActiveRiders.ToString(),
                                ["Total Shifts"] = data.Shifts.TotalShifts.ToString(),
                                ["Completion Rate"] = $"{data.Shifts.CompletionRate:F1}%"
                            }
                        }
                    }
                },

                new PdfSection
                {
                    Title = "Company Performance",
                    Contents = new List<PdfContent>
                    {
                        new()
                        {
                            Type = PdfContentType.Table,
                            Data = new TableData
                            {
                                Headers = new List<string>
                                {
                                    "Company", "Shifts", "Orders", "Score %"
                                },
                                Rows = data.Companies.CompanyDetails
                                    .OrderByDescending(c => c.PerformanceScore)
                                    .Select(c => new List<object>
                                    {
                                        c.CompanyName,
                                        c.TotalShifts,
                                        c.TotalAcceptedOrders,
                                        $"{c.PerformanceScore:F1}%"
                                    }).ToList()
                            }
                        }
                    }
                },

                new PdfSection
                {
                    Title = "Top Performers",
                    Contents = new List<PdfContent>
                    {
                        new()
                        {
                            Type = PdfContentType.Table,
                            Data = new TableData
                            {
                                Headers = new List<string>
                                {
                                    "#", "Rider", "Orders", "Score"
                                },
                                Rows = data.Performance.TopPerformers.Take(10)
                                    .Select((p, i) => new List<object>
                                    {
                                        i + 1,
                                        p.RiderName,
                                        p.TotalOrders,
                                        $"{p.PerformanceScore:F1}%"
                                    }).ToList()
                            }
                        }
                    }
                }
            }
        };
    }
}

// ============= MAIN EXPORT SERVICE =============

