using Application.Contracts.Notifications;
using System.Globalization;

namespace Application.Service.Notifications;

public static class NotificationTemplates
{
    public static SendNotificationRequest RejectedOrders(DateOnly date, long count)
    {
        var number = count.ToString(CultureInfo.InvariantCulture);
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(new()
        {
            ["en"] = new("Yesterday's rejected orders",
                $"You had {number} real rejected orders yesterday ({day}). Please be careful today, review each order, and avoid unnecessary rejections."),
            ["ar"] = new("الطلبات المرفوضة أمس",
                $"لديك {number} من حالات الرفض الفعلية للطلبات أمس ({day}). انتبه اليوم، وراجع كل طلب بعناية، وتجنب الرفض غير الضروري. نثق بقدرتك على التحسن."),
            ["bn"] = new("গতকালের প্রত্যাখ্যাত অর্ডার",
                $"গতকাল ({day}) আপনার প্রকৃত প্রত্যাখ্যাত অর্ডার ছিল {number}টি। আজ সতর্ক থাকুন, প্রতিটি অর্ডার ভালোভাবে দেখুন এবং অপ্রয়োজনীয় প্রত্যাখ্যান এড়িয়ে চলুন। আপনি আরও ভালো করতে পারবেন।")
        }, ReportAction(date));
    }

    public static SendNotificationRequest RankChanged(DateOnly date, string previous, string current)
    {
        var improved = current[0] < previous[0]; // A is highest, F is lowest.
        return new(improved ? new()
        {
            ["en"] = new("Your rank improved!",
                $"Great work! Your rank moved up from {previous} to {current}. Keep this energy, stay consistent, and build on your progress!"),
            ["ar"] = new("تصنيفك في تحسن!",
                $"أحسنت! ارتفع تصنيفك من {previous} إلى {current}. واصل بهذه الروح، وحافظ على أدائك المميز. نعتز بتقدمك!"),
            ["bn"] = new("আপনার র‍্যাঙ্ক উন্নত হয়েছে!",
                $"দারুণ কাজ! আপনার র‍্যাঙ্ক {previous} থেকে {current}-এ উঠেছে। এই উদ্যম ধরে রাখুন, নিয়মিত ভালো কাজ করুন এবং আরও এগিয়ে যান!")
        } : new Dictionary<string, NotificationContent>
        {
            ["en"] = new("You can bounce back!",
                $"Your rank moved down from {previous} to {current}. You can improve! Focus on on-time deliveries, careful handling, and fewer rejections. We believe in your progress."),
            ["ar"] = new("تستطيع العودة أقوى!",
                $"انخفض تصنيفك من {previous} إلى {current}، لكنك قادر على التحسن! ركز على التوصيل في الوقت المحدد، وجودة الخدمة، وتقليل الرفض. نثق بقدرتك على التقدم."),
            ["bn"] = new("আপনি আবার এগিয়ে যেতে পারবেন!",
                $"আপনার র‍্যাঙ্ক {previous} থেকে {current}-এ নেমেছে। আপনি উন্নতি করতে পারবেন! সময়মতো ডেলিভারি, যত্নসহকারে কাজ এবং কম অর্ডার প্রত্যাখ্যানে মনোযোগ দিন। আপনার ওপর আমাদের বিশ্বাস আছে।")
        }, ReportAction(date));
    }

    public static SendNotificationRequest OnTimeDeliveryGoal(DateOnly date, bool targetReached) => new(
        targetReached ? new()
        {
            ["en"] = new("Keep up your on-time deliveries!",
                "Great work! Your on-time delivery rate is at least 99%. Keep going, stay consistent, and maintain this excellent performance!"),
            ["ar"] = new("واصل تميزك في التوصيل!",
                "أحسنت! نسبة التوصيل في الوقت المحدد لديك 99% أو أكثر. واصل بهذا المستوى، وحافظ على انتظامك وأدائك المميز!"),
            ["bn"] = new("সময়মতো ডেলিভারি চালিয়ে যান!",
                "দারুণ কাজ! আপনার সময়মতো ডেলিভারির হার 99% বা তার বেশি। এগিয়ে যান, ধারাবাহিকতা বজায় রাখুন এবং এই চমৎকার কাজ চালিয়ে যান!")
        } : new Dictionary<string, NotificationContent>
        {
            ["en"] = new("Let's reach 99% on-time delivery!",
                "Aim for 99% on-time delivery! Plan your route, collect orders promptly, and deliver safely and on time. Every delivery helps you improve. You can do it!"),
            ["ar"] = new("معًا نصل إلى 99% في التوصيل!",
                "اجعل هدفك 99% للتوصيل في الوقت المحدد! خطط لمسارك، واستلم الطلبات دون تأخير، ووصلها بأمان وفي موعدها. كل طلب فرصة للتحسن. نثق بقدرتك!"),
            ["bn"] = new("সময়মতো ডেলিভারিতে 99% অর্জন করি!",
                "সময়মতো ডেলিভারিতে 99% লক্ষ্য রাখুন! রুট পরিকল্পনা করুন, দ্রুত অর্ডার সংগ্রহ করুন এবং নিরাপদে ও সময়মতো পৌঁছে দিন। প্রতিটি ডেলিভারিতে উন্নতির সুযোগ আছে। আপনি পারবেন!")
        }, ReportAction(date));

    private static Dictionary<string, string> ReportAction(DateOnly date) => new()
    {
        ["type"] = "performance_report", ["year"] = date.Year.ToString(CultureInfo.InvariantCulture),
        ["month"] = date.Month.ToString(CultureInfo.InvariantCulture)
    };

    public static SendNotificationRequest ProfileChanged => new(new()
    {
        ["en"] = new("Profile updated", "Your app profile has been updated."),
        ["ar"] = new("تحديث الملف الشخصي", "تم تحديث ملفك الشخصي في التطبيق."),
        ["bn"] = new("প্রোফাইল আপডেট", "অ্যাপে আপনার প্রোফাইল আপডেট করা হয়েছে।")
    }, new() { ["type"] = "profile" });

    public static SendNotificationRequest DailyReminder => new(new()
    {
        ["en"] = new("Daily reminder", "Check your latest updates in the app."),
        ["ar"] = new("تذكير يومي", "راجع آخر التحديثات في التطبيق."),
        ["bn"] = new("দৈনিক অনুস্মারক", "অ্যাপে আপনার সর্বশেষ আপডেট দেখুন।")
    }, new() { ["type"] = "home" });
}
