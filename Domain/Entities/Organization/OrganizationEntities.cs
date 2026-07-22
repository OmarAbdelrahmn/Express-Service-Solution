namespace Domain.Entities.Organization;

public class Tenant
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LegalEntity> LegalEntities { get; set; } = [];
}

public class LegalEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string BaseCurrencyCode { get; set; } = "SAR";
    public string? TaxRegistrationNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<Branch> Branches { get; set; } = [];
    public ICollection<PlatformAccount> PlatformAccounts { get; set; } = [];
}

public class Branch
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public LegalEntity LegalEntity { get; set; } = null!;
}

public class PlatformAccount
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string? ExternalAccountReference { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<LegacyCompanyPlatformMapping> LegacyCompanyMappings { get; set; } = [];
}

public class LegacyCompanyPlatformMapping
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int PlatformAccountId { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public PlatformAccount PlatformAccount { get; set; } = null!;
}
