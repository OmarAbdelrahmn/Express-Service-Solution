namespace Application.Abstraction.Errors;

public static class DailyWorkErrors
{
    public static readonly Error InvalidDate = new(
        "DailyWork.InvalidDate", "A date is required.", 400);
    public static readonly Error InvalidItems = new(
        "DailyWork.InvalidItems", "Supply between 1 and 1000 items.", 400);
    public static readonly Error InvalidItem = new(
        "DailyWork.InvalidItem",
        "Each item must have exactly one positive rider ID or employee Iqama number, nonnegative orders, and working hours between 0 and 24 with at most two decimal places.", 400);
    public static readonly Error PersonNotFound = new(
        "DailyWork.PersonNotFound", "One or more rider IDs or employee Iqama numbers do not exist.", 404);
    public static readonly Error DuplicateEmployee = new(
        "DailyWork.DuplicateEmployee", "The same employee cannot appear more than once, including through a rider ID.", 400);
}
