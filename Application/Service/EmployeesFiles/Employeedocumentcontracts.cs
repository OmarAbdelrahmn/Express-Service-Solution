using Microsoft.AspNetCore.Http;

namespace Application.Service.EmployeesFiles;

public enum EmployeeImageType
{
    ProfileImage = 1,
    PassportImage = 2,
    IqamaImage = 3,
    LicenseImage = 4,
    WorkPermitImage = 5,
    AdditionalImage1 = 6,
    AdditionalImage2 = 7,
    AdditionalImage3 = 8,
    AdditionalImage4 = 9
}

/// <summary>
/// Upload multiple image slots in one multipart/form-data request.
/// Any null field is silently skipped.
/// </summary>
public record UploadAllImagesRequest(
    long IqamaNo,
    IFormFile? ProfileImage,
    IFormFile? PassportImage,
    IFormFile? IqamaImage,
    IFormFile? LicenseImage,
    IFormFile? WorkPermitImage,
    IFormFile? AdditionalImage1,
    IFormFile? AdditionalImage2,
    IFormFile? AdditionalImage3,
    IFormFile? AdditionalImage4
);

/// <summary>All 9 image URLs for one employee.</summary>
public record EmployeeDocumentsResponse(
    long IqamaNo,
    string? ProfileImageUrl,
    string? PassportImageUrl,
    string? IqamaImageUrl,
    string? LicenseImageUrl,
    string? WorkPermitImageUrl,
    string? AdditionalImage1Url,
    string? AdditionalImage2Url,
    string? AdditionalImage3Url,
    string? AdditionalImage4Url
);

/// <summary>Lightweight response used in avatar/listing endpoints.</summary>
public record EmployeeMainImageResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string? ProfileImageUrl
);

/// <summary>Returned after a single-image upload or soft-delete.</summary>
public record ImageOperationResponse(
    long IqamaNo,
    EmployeeImageType ImageType,
    string? ImageUrl,
    string Message
);