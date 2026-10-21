using System.Globalization;

namespace Application.Service.RiderMobile;

internal static class RiderMobileText
{
    private static readonly CultureInfo Arabic = CreateArabicCulture();
    public static bool IsLanguage(string? language) => language is "ar" or "en" or "bn";
    public static CultureInfo Culture(string language) => language == "ar" ? Arabic :
        CultureInfo.GetCultureInfo(language == "bn" ? "bn-BD" : "en-GB");

    private static CultureInfo CreateArabicCulture()
    {
        var culture = new CultureInfo("ar-SA");
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return CultureInfo.ReadOnly(culture);
    }

    public static string Get(string language, string key, params object[] values)
    {
        var text = Texts[key];
        return string.Format(Culture(language), language switch { "ar" => text.Ar, "bn" => text.Bn, _ => text.En }, values);
    }

    private static readonly Dictionary<string, (string En, string Ar, string Bn)> Texts = new()
    {
        ["InvalidLogin"] = ("Unable to sign in with these details.", "تعذر تسجيل الدخول بهذه البيانات.", "এই তথ্য দিয়ে সাইন ইন করা যায়নি।"),
        ["InvalidLanguage"] = ("Language must be ar, en or bn.", "يجب أن تكون اللغة ar أو en أو bn.", "ভাষা ar, en অথবা bn হতে হবে।"),
        ["InvalidSession"] = ("Your session has ended. Please sign in again.", "انتهت الجلسة. يرجى تسجيل الدخول مجدداً.", "আপনার সেশনের মেয়াদ শেষ হয়েছে। আবার সাইন ইন করুন।"),
        ["InvalidMonth"] = ("Choose a valid current or past month (year 2000 or later).", "اختر شهراً حالياً أو سابقاً صالحاً من سنة 2000 أو بعدها.", "২০০০ সাল বা তার পরের বর্তমান বা অতীতের একটি বৈধ মাস বেছে নিন।"),
        ["Future"] = ("Upcoming", "قادم", "আসন্ন"),
        ["InProgress"] = ("Today — in progress", "اليوم — جارٍ العمل", "আজ — চলমান"),
        ["Missing"] = ("No shift data", "لا توجد بيانات وردية", "শিফটের তথ্য নেই"),
        ["DayOff"] = ("Recorded day off", "يوم راحة مسجل", "নথিভুক্ত ছুটির দিন"),
        ["ShortHours"] = ("Below 8 hours", "أقل من 8 ساعات", "৮ ঘণ্টার কম"),
        ["Qualified"] = ("8-hour target achieved", "تحقق هدف 8 ساعات", "৮ ঘণ্টার লক্ষ্য পূরণ হয়েছে"),
        ["RestrictedWeekday"] = ("Days off are not allowed on Thursday, Friday or Saturday.", "لا يسمح بالراحة أيام الخميس والجمعة والسبت.", "বৃহস্পতিবার, শুক্রবার বা শনিবার ছুটি নেওয়া যায় না।"),
        ["FirstThreeDays"] = ("Days off are not allowed during the first 3 days of the month.", "لا يسمح بالراحة في أول 3 أيام من الشهر.", "মাসের প্রথম ৩ দিন ছুটি নেওয়া যায় না।"),
        ["LastThreeDays"] = ("Days off are not allowed during the last 3 days of the month.", "لا يسمح بالراحة في آخر 3 أيام من الشهر.", "মাসের শেষ ৩ দিন ছুটি নেওয়া যায় না।"),
        ["OnTime"] = ("On-time deliveries: {0:0.##}%; coaching target: {1:0.##}%.", "التوصيل في الوقت المحدد: {0:0.##}٪؛ الهدف الإرشادي: {1:0.##}٪.", "সময়মতো ডেলিভারি: {0:0.##}%; পরামর্শমূলক লক্ষ্য: {1:0.##}%।"),
        ["OnTimeAdvice"] = ("Check the route before leaving, collect orders promptly, and report restaurant or traffic delays through the platform. Follow road safety rules.", "راجع المسار قبل الانطلاق، واستلم الطلبات دون تأخير، وأبلغ المنصة عن تأخر المطعم أو الازدحام. التزم بالسلامة المرورية.", "রওনা হওয়ার আগে পথ দেখুন, দ্রুত অর্ডার সংগ্রহ করুন এবং রেস্তোরাঁ বা যানজটের বিলম্ব প্ল্যাটফর্মে জানান। সড়ক নিরাপত্তা মেনে চলুন।"),
        ["Verification"] = ("Successful verifications: {0:0.##}%; coaching target: {1:0.##}%.", "نجاح التحقق: {0:0.##}٪؛ الهدف الإرشادي: {1:0.##}٪.", "সফল যাচাইকরণ: {0:0.##}%; পরামর্শমূলক লক্ষ্য: {1:0.##}%।"),
        ["VerificationAdvice"] = ("Complete each verification request promptly, use clear photos, and keep location and camera permissions enabled.", "أكمل طلبات التحقق فور وصولها، واستخدم صوراً واضحة، وفعّل أذونات الموقع والكاميرا.", "প্রতিটি যাচাইকরণের অনুরোধ দ্রুত সম্পন্ন করুন, পরিষ্কার ছবি ব্যবহার করুন এবং অবস্থান ও ক্যামেরার অনুমতি চালু রাখুন।"),
        ["Quality"] = ("Latest delivery quality score: {0:0.##}%; coaching target: {1:0.##}%.", "آخر درجة لجودة التوصيل: {0:0.##}٪؛ الهدف الإرشادي: {1:0.##}٪.", "সর্বশেষ ডেলিভারির মানের স্কোর: {0:0.##}%; পরামর্শমূলক লক্ষ্য: {1:0.##}%।"),
        ["QualityAdvice"] = ("Check the order and address, protect the package, follow delivery instructions, and confirm delivery only after handing over the order.", "تحقق من الطلب والعنوان، وحافظ على التغليف، واتبع تعليمات العميل، ولا تؤكد التسليم إلا بعد تسليم الطلب.", "অর্ডার ও ঠিকানা যাচাই করুন, প্যাকেট সুরক্ষিত রাখুন, ডেলিভারির নির্দেশনা মানুন এবং অর্ডার হস্তান্তরের পরেই ডেলিভারি নিশ্চিত করুন।"),
        ["FailedOrders"] = ("Orders failed by rider: {0:0}.", "طلبات فشلت بسبب المندوب: {0:0}.", "রাইডারের কারণে ব্যর্থ অর্ডার: {0:0}।"),
        ["FailedOrdersAdvice"] = ("Review failed orders with your supervisor, contact the customer when needed, and ask support for help before an order fails.", "راجع الطلبات الفاشلة مع المشرف، وتواصل مع العميل عند الحاجة، واطلب مساعدة الدعم قبل فشل الطلب.", "সুপারভাইজারের সঙ্গে ব্যর্থ অর্ডার পর্যালোচনা করুন, প্রয়োজনে গ্রাহকের সঙ্গে যোগাযোগ করুন এবং অর্ডার ব্যর্থ হওয়ার আগে সহায়তা নিন।"),
        ["WorkingDays"] = ("Recorded qualified days: {0:0}; monthly target: {1:0}.", "أيام العمل المسجلة المستوفية للساعات: {0:0}؛ الهدف الشهري: {1:0}.", "নথিভুক্ত পূর্ণ কর্মদিবস: {0:0}; মাসিক লক্ষ্য: {1:0}।"),
        ["WorkingDaysAdvice"] = ("Plan at least 26 days of 8 hours each. Extra hours on one day do not replace another qualified day. Check missing records with your supervisor.", "خطط للعمل 26 يوماً على الأقل بواقع 8 ساعات يومياً. الساعات الإضافية في يوم لا تعوض يوم عمل آخر. راجع البيانات الناقصة مع المشرف.", "প্রতিদিন ৮ ঘণ্টা করে অন্তত ২৬ দিন কাজের পরিকল্পনা করুন। একদিনের অতিরিক্ত ঘণ্টা অন্য পূর্ণ কর্মদিবসের বিকল্প নয়। অনুপস্থিত রেকর্ড সুপারভাইজারের সঙ্গে যাচাই করুন।"),
        ["DailyHours"] = ("Recorded hours: {0:0.##}; daily target: {1:0.##}.", "ساعات العمل المسجلة: {0:0.##}؛ الهدف اليومي: {1:0.##}.", "নথিভুক্ত ঘণ্টা: {0:0.##}; দৈনিক লক্ষ্য: {1:0.##}।"),
        ["DailyHoursAdvice"] = ("Complete at least 8 working hours on each working day. If the record is incorrect, ask your supervisor to check the shift import.", "أكمل 8 ساعات عمل على الأقل في كل يوم عمل. إذا كانت البيانات غير صحيحة، اطلب من المشرف مراجعة استيراد الوردية.", "প্রতিটি কর্মদিবসে অন্তত ৮ ঘণ্টা কাজ করুন। রেকর্ড ভুল হলে সুপারভাইজারকে শিফটের আমদানি করা তথ্য যাচাই করতে বলুন।"),
        ["ProhibitedBreak"] = ("A day off was recorded on a prohibited break date.", "سُجّل يوم راحة في تاريخ لا يسمح فيه بالراحة.", "ছুটি নিষিদ্ধ এমন তারিখে ছুটি নথিভুক্ত হয়েছে।"),
        ["ProhibitedBreakAdvice"] = ("Arrange days off outside Thursday, Friday, Saturday, and the first and last 3 days of the month. Confirm approval with your supervisor.", "رتب أيام الراحة خارج الخميس والجمعة والسبت، وخارج أول وآخر 3 أيام من الشهر. أكد الموافقة مع المشرف.", "বৃহস্পতিবার, শুক্রবার, শনিবার এবং মাসের প্রথম ও শেষ ৩ দিন বাদ দিয়ে ছুটির ব্যবস্থা করুন। সুপারভাইজারের অনুমোদন নিশ্চিত করুন।"),
        ["MissingData"] = ("Completed calendar days without shift data: {0:0}.", "أيام منتهية دون بيانات وردية: {0:0}.", "শিফটের তথ্য ছাড়া অতিবাহিত দিন: {0:0}।"),
        ["MissingDataAdvice"] = ("Ask your supervisor to check missing shift records. Missing data is not proof of absence or an approved break.", "اطلب من المشرف مراجعة سجلات الورديات الناقصة. غياب البيانات لا يثبت الغياب أو الموافقة على الراحة.", "সুপারভাইজারকে অনুপস্থিত শিফট রেকর্ড যাচাই করতে বলুন। তথ্য না থাকা অনুপস্থিতি বা অনুমোদিত ছুটির প্রমাণ নয়।"),
        ["NoPerformance"] = ("No performance scores are available for this month yet.", "لا توجد درجات أداء لهذا الشهر حتى الآن.", "এই মাসের পারফরম্যান্স স্কোর এখনো পাওয়া যায়নি।"),
        ["NoPerformanceAdvice"] = ("Keep following the work targets and ask your supervisor when performance data will be updated.", "استمر في الالتزام بأهداف العمل، واستفسر من المشرف عن موعد تحديث بيانات الأداء.", "কাজের লক্ষ্য অনুসরণ করুন এবং পারফরম্যান্সের তথ্য কখন হালনাগাদ হবে তা সুপারভাইজারকে জিজ্ঞাসা করুন।"),
        ["RankBelowA"] = ("Your latest imported rank is below A.", "آخر تصنيف مستورد لك أقل من A.", "আপনার সর্বশেষ আমদানি করা র‍্যাঙ্ক A-এর নিচে।"),
        ["RankBelowAAdvice"] = ("Focus on the flagged metrics and review progress with your supervisor. Coaching targets guide improvement; the platform determines your official rank.", "ركز على مؤشرات التحسين الموضحة وراجع تقدمك مع المشرف. الأهداف الإرشادية تساعدك على التحسن، والمنصة تحدد تصنيفك الرسمي.", "চিহ্নিত সূচকগুলো উন্নত করুন এবং সুপারভাইজারের সঙ্গে অগ্রগতি পর্যালোচনা করুন। পরামর্শমূলক লক্ষ্য উন্নতিতে সহায়তা করে; অফিসিয়াল র‍্যাঙ্ক প্ল্যাটফর্ম নির্ধারণ করে।"),
        ["MaintainA"] = ("Your latest imported rank is A.", "آخر تصنيف مستورد لك هو A.", "আপনার সর্বশেষ আমদানি করা র‍্যাঙ্ক A।"),
        ["MaintainAAdvice"] = ("Maintain consistent hours, timely deliveries and careful order handling, and keep checking your progress.", "حافظ على انتظام الساعات والتوصيل في الموعد والعناية بالطلبات، واستمر في متابعة تقدمك.", "নিয়মিত কর্মঘণ্টা, সময়মতো ডেলিভারি ও যত্নসহকারে অর্ডার পরিচালনা বজায় রাখুন এবং অগ্রগতি দেখুন।"),
        ["Unreachable"] = ("The remaining calendar days cannot bring recorded qualified days to 26.", "الأيام المتبقية لا تكفي للوصول إلى 26 يوم عمل مستوفياً للساعات وفق السجلات الحالية.", "নথিভুক্ত পূর্ণ কর্মদিবস ২৬-এ নিতে অবশিষ্ট দিন যথেষ্ট নয়।"),
        ["UnreachableAdvice"] = ("Review missing or short shifts with your supervisor and plan the remaining working days. Do not try to compensate by unsafe riding.", "راجع الورديات الناقصة أو القصيرة مع المشرف وخطط لأيام العمل المتبقية. لا تحاول التعويض بالقيادة غير الآمنة.", "সুপারভাইজারের সঙ্গে অনুপস্থিত বা কম ঘণ্টার শিফট যাচাই করে বাকি দিনের পরিকল্পনা করুন। অনিরাপদভাবে গাড়ি চালিয়ে ঘাটতি পূরণের চেষ্টা করবেন না।"),
        ["BreakLimit"] = ("Recorded days off: {0:0}; maximum under the monthly rules: {1:0}.", "أيام الراحة المسجلة: {0:0}؛ الحد الأقصى وفق القواعد الشهرية: {1:0}.", "নথিভুক্ত ছুটি: {0:0}; মাসিক নিয়মে সর্বোচ্চ: {1:0}।"),
        ["BreakLimitAdvice"] = ("Coordinate breaks with your supervisor and keep enough days available to meet the 26-day target. The existing scheduler allows at most 3 breaks per month.", "نسق الراحة مع المشرف واترك أياماً كافية لتحقيق هدف 26 يوماً. نظام الجدولة الحالي يسمح بثلاث راحات شهرياً كحد أقصى.", "সুপারভাইজারের সঙ্গে ছুটি সমন্বয় করুন এবং ২৬ দিনের লক্ষ্য পূরণের জন্য পর্যাপ্ত দিন রাখুন। বর্তমান সময়সূচি মাসে সর্বোচ্চ ৩ দিন ছুটি দেয়।")
    };
}
