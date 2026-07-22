using System.Text.RegularExpressions;
using Domain.Entities.AccountingPlatform;

namespace Application.Contracts.PlatformImports;

/// <summary>
/// Arabic display text for the accountant import workspace. Codes remain stable
/// integration values; this class is deliberately presentation-only.
/// </summary>
public static partial class AccountingImportArabicText
{
    public static string Metric(string? code) => Normalize(code) switch
    {
        "ACCEPTED_ORDERS" => "الطلبات المكتملة",
        "WORK_DAYS" => "أيام العمل",
        "BASE_AMOUNT" => "المبلغ الأساسي",
        "INCENTIVES" => "الحوافز",
        "PENALTIES" => "الخصومات والغرامات",
        "FEES" => "الرسوم",
        "VAT" => "ضريبة القيمة المضافة",
        "COMPANY_TOTAL" => "إجمالي مستحقات الشركة",
        "VALIDITY" => "حالة الاستحقاق",
        "RIDER_PAYOUT" => "مستحقات المندوب",
        "NET_SETTLEMENT" => "صافي التسوية",
        "INVOICE_AMOUNT" => "مبلغ الفاتورة",
        "EID_DAYS" => "أيام العيد",
        "EID_OVERTIME_AMOUNT" => "بدل العمل الإضافي للعيد",
        "DISTANCE_KM" => "مسافة التوصيل (كم)",
        "CONNECTION_HOURS" => "ساعات الاتصال",
        _ => string.IsNullOrWhiteSpace(code) ? "غير محدد" : code
    };

    public static string FactCategory(PlatformFactCategory category) => category switch
    {
        PlatformFactCategory.Activity => "النشاط",
        PlatformFactCategory.RiderPayout => "مستحقات المندوب",
        PlatformFactCategory.CompanyBilling => "فوترة الشركة",
        PlatformFactCategory.Tax => "الضرائب",
        PlatformFactCategory.Payout => "الدفعات",
        PlatformFactCategory.Validity => "التحقق من الاستحقاق",
        PlatformFactCategory.Penalty => "الخصومات",
        PlatformFactCategory.ControlTotal => "إجمالي المطابقة",
        _ => "غير محدد"
    };

    public static string WorkerCategory(string? category) => Normalize(category) switch
    {
        "RIDER" => "مندوب",
        "COMPANY" => "الشركة",
        "AMAZON" => "مندوب أمازون",
        "HUNGER" => "مندوب هنقرستيشن",
        "KEETAPAYPERORDER" => "مندوب كيتا (الدفع بالطلب)",
        "KEETASEGMENTS" => "مندوب كيتا (نظام الشرائح)",
        _ => string.IsNullOrWhiteSpace(category) ? "غير محدد" : category
    };

    public static string ImportStatus(PlatformImportStatus status) => status switch
    {
        PlatformImportStatus.Received => "تم الاستلام",
        PlatformImportStatus.Parsing => "جارٍ تحليل الملف",
        PlatformImportStatus.NeedsResolution => "تحتاج إلى معالجة",
        PlatformImportStatus.Reconciled => "تمت المطابقة",
        PlatformImportStatus.Approved => "معتمدة",
        PlatformImportStatus.Rejected => "مرفوضة",
        PlatformImportStatus.Superseded => "تم استبدالها",
        PlatformImportStatus.Failed => "فشل الاستيراد",
        _ => "غير محدد"
    };

    public static string TemplateStatus(PlatformTemplateStatus status) => status switch
    {
        PlatformTemplateStatus.Draft => "مسودة",
        PlatformTemplateStatus.Active => "نشط",
        PlatformTemplateStatus.Retired => "متقاعد",
        _ => "غير محدد"
    };

    public static string IssueSeverity(PlatformImportIssueSeverity severity) => severity switch
    {
        PlatformImportIssueSeverity.Warning => "تحذير",
        PlatformImportIssueSeverity.Blocking => "مانع",
        _ => "غير محدد"
    };

    public static string IssueStatus(PlatformImportIssueStatus status) => status switch
    {
        PlatformImportIssueStatus.Open => "مفتوحة",
        PlatformImportIssueStatus.Resolved => "تمت المعالجة",
        PlatformImportIssueStatus.Waived => "تم التجاوز عنها",
        _ => "غير محدد"
    };

    public static string IssueCode(string? code) => Normalize(code) switch
    {
        "SCHEMA_DRIFT" => "بنية الملف لا تطابق القالب المعتمد",
        "ADAPTER_NOT_INSTALLED" => "معالج المنصة غير متاح",
        "SHEET_MISSING" => "ورقة البيانات المطلوبة غير موجودة",
        "HEADER_MISSING" => "صف العناوين غير موجود",
        "WORKER_COLUMN_MISSING" => "عمود المندوب غير موجود",
        "METRIC_COLUMN_MISSING" => "عمود قيمة محاسبية مطلوب غير موجود",
        "IDENTITY_MISSING" => "هوية المندوب غير مرتبطة",
        "IDENTITY_AMBIGUOUS" => "هوية المندوب مرتبطة بأكثر من سجل",
        "METRIC_NOT_ALLOWED" => "القيمة المحاسبية غير مسموح بها",
        "VALUE_INVALID" => "قيمة الخلية غير صالحة",
        "CONTROL_TOTAL_MISMATCH" => "إجمالي المصدر لا يطابق الإجمالي المحسوب",
        "COMPANY_SHEET_MISSING" => "ورقة ملخص الشركة غير موجودة",
        "COMPANY_VALUE_MISSING" => "قيمة ملخص الشركة غير موجودة أو غير صالحة",
        "WORKBOOK_PROFILE_MISMATCH" => "الملف لا يطابق نموذج المنصة المختار",
        _ => string.IsNullOrWhiteSpace(code) ? "مشكلة غير محددة" : code
    };

    public static string IssueMessage(string? code, string? storedMessage)
    {
        if (string.IsNullOrWhiteSpace(storedMessage)) return IssueCode(code);
        if (storedMessage.StartsWith("المندوب", StringComparison.Ordinal)) return storedMessage;

        var identity = IdentityMessageRegex().Match(storedMessage);
        if (identity.Success)
        {
            var source = string.Equals(identity.Groups["source"].Value, "None", StringComparison.OrdinalIgnoreCase)
                ? "لا يوجد"
                : identity.Groups["source"].Value;
            return $"المندوب {identity.Groups["worker"].Value} لديه {identity.Groups["matches"].Value} تطابق فعّال للهوية بتاريخ {identity.Groups["date"].Value}. المصادر: {source}.";
        }

        // Legacy rows may have been saved before Arabic messages were introduced.
        // The code remains the authoritative integration value; this gives the UI an Arabic explanation now.
        return IssueCode(code);
    }

    public static string IdentityIssueMessage(string workerId, int matchCount, DateOnly factDate, string source) =>
        $"المندوب {workerId} لديه {matchCount} تطابق فعّال للهوية بتاريخ {factDate:yyyy-MM-dd}. المصادر: {(string.Equals(source, "None", StringComparison.OrdinalIgnoreCase) ? "لا يوجد" : source)}.";

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    [GeneratedRegex(@"^Worker (?<worker>.+?) has (?<matches>\d+) effective identity matches on (?<date>\d{4}-\d{2}-\d{2})\. Sources: (?<source>.+)\.$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityMessageRegex();
}
