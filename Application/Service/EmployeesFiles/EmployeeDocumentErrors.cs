using Application.Abstraction;
using Microsoft.AspNetCore.Http;

namespace Application.Service.EmployeesFiles;

public static class EmployeeDocumentErrors
{
    public static readonly Error EmployeeNotFound = new(
        "Employee.NotFound",
        "No active employee was found with the given Iqama number.",
        StatusCodes.Status404NotFound);

    public static readonly Error DocumentsNotFound = new(
        "EmployeeDocuments.NotFound",
        "No document record exists for this employee. Upload an image first.",
        StatusCodes.Status404NotFound);
}