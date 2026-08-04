using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class KeetaBreakErrors
{
    public static readonly Error NotFound = new("KeetaBreak.NotFound", "لم يتم العثور على بيانات جدول الراحات.", StatusCodes.Status404NotFound);
    public static readonly Error InvalidRequest = new("KeetaBreak.InvalidRequest", "بيانات جدول الراحات غير صحيحة.", StatusCodes.Status400BadRequest);
    public static readonly Error InvalidFile = new("KeetaBreak.InvalidFile", "يرجى رفع ملف Excel بصيغة .xlsx.", StatusCodes.Status400BadRequest);
    public static readonly Error DuplicatePeriod = new("KeetaBreak.DuplicatePeriod", "توجد فترة مستوردة ومؤكدة متداخلة بالفعل.", StatusCodes.Status409Conflict);
    public static readonly Error InvalidState = new("KeetaBreak.InvalidState", "لا يمكن تنفيذ العملية في حالة الدفعة الحالية.", StatusCodes.Status409Conflict);
    public static readonly Error NoConfiguration = new("KeetaBreak.NoConfiguration", "لا يوجد إعداد شفتات فعّال يغطي فترة الاستيراد كاملة.", StatusCodes.Status409Conflict);
    public static readonly Error ValidationFailed = new("KeetaBreak.ValidationFailed", "تعذر اعتماد جدول الراحات لأن السعة أو الحدود تغيرت. حدّث البيانات وأنشئ المسودة من جديد.", StatusCodes.Status409Conflict);
    public static readonly Error ConcurrentUpdate = new("KeetaBreak.ConcurrentUpdate", "تم تعديل جدول الراحات بواسطة مستخدم آخر. حدّث الصفحة وحاول مرة أخرى.", StatusCodes.Status409Conflict);
    public static readonly Error CannotDelete = new("KeetaBreak.CannotDelete", "تعذر حذف هذا الإصدار من إعدادات الراحات لارتباطه ببيانات أخرى.", StatusCodes.Status409Conflict);
}
