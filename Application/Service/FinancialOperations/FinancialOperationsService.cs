using System.Data;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.FinancialOperations;
using Application.Contracts.Ledger;
using Application.Service.AccountingPosting;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Domain.Entities.FinancialOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Service.FinancialOperations;

public class FinancialOperationsService(ApplicationDbcontext dbcontext, IFinancialAccessService financialAccessService, IAccountingPostingService accountingPostingService) : IFinancialOperationsService
{
    public async Task<Result<PagedResponse<MasterRecordResponse>>> GetCustomerAccountsAsync(PaginationRequest pagination, MasterRecordListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<MasterRecordResponse>>(error);
        var query = dbcontext.CustomerAccounts.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Active.HasValue) query = query.Where(x => x.IsActive == filter.Active.Value);
        query = ApplyCreatedDateFilter(query, filter.FromDate, filter.ToDate, x => x.CreatedAt);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term) || (x.TaxRegistrationNumber != null && x.TaxRegistrationNumber.ToLower().Contains(term))); }
        var sort = SortProperty(filter.SortBy, "CreatedAt", ("code", "Code"), ("name", "Name"), ("active", "IsActive"), ("status", "IsActive"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<MasterRecordResponse>> GetCustomerAccountAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.CustomerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<MasterRecordResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetCustomerInvoicesAsync(PaginationRequest pagination, CustomerInvoiceListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.CustomerInvoices.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.CustomerAccountId.HasValue) query = query.Where(x => x.CustomerAccountId == filter.CustomerAccountId.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.InvoiceDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.InvoiceDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.InvoiceNumber.ToLower().Contains(term) || x.CustomerAccount.Code.ToLower().Contains(term) || x.CustomerAccount.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "InvoiceDate", ("number", "InvoiceNumber"), ("status", "Status"), ("amount", "GrossAmount"), ("duedate", "DueDate"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, x => ToResponse(x), ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetCustomerInvoiceAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var invoice = await dbcontext.CustomerInvoices.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (invoice is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, invoice.LegalEntityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var allocated = await dbcontext.CustomerReceiptAllocations.AsNoTracking().Where(x => x.CustomerInvoiceId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        return Result.Success(ToResponse(invoice, Math.Max(0m, invoice.GrossAmount - allocated)));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetCustomerReceiptsAsync(PaginationRequest pagination, CustomerReceiptListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.CustomerReceipts.AsNoTracking().Include(x => x.Allocations).Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.CustomerAccountId.HasValue) query = query.Where(x => x.CustomerAccountId == filter.CustomerAccountId.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.ReceiptDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.ReceiptDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.ReceiptNumber.ToLower().Contains(term) || x.ExternalReference.ToLower().Contains(term) || x.CustomerAccount.Code.ToLower().Contains(term) || x.CustomerAccount.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "ReceiptDate", ("number", "ReceiptNumber"), ("status", "Status"), ("amount", "Amount"), ("reference", "ExternalReference"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, x => ToResponse(x), ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetCustomerReceiptAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var receipt = await dbcontext.CustomerReceipts.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, receipt.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FinancialOperationResponse>(access.Error) : Result.Success(ToResponse(receipt));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetPlatformSettlementsAsync(PaginationRequest pagination, PlatformSettlementListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.PlatformSettlements.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.SettlementDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.SettlementDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.SettlementReference.ToLower().Contains(term) || x.SourceEvidence.ExternalReference.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "SettlementDate", ("number", "SettlementReference"), ("reference", "SettlementReference"), ("status", "Status"), ("amount", "NetSettlementAmount"), ("grossamount", "GrossRevenue"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, x => ToResponse(x), ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetPlatformSettlementAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.PlatformSettlements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FinancialOperationResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<MasterRecordResponse>>> GetSupplierAccountsAsync(PaginationRequest pagination, MasterRecordListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<MasterRecordResponse>>(error);
        var query = dbcontext.SupplierAccounts.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Active.HasValue) query = query.Where(x => x.IsActive == filter.Active.Value);
        query = ApplyCreatedDateFilter(query, filter.FromDate, filter.ToDate, x => x.CreatedAt);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term) || (x.TaxRegistrationNumber != null && x.TaxRegistrationNumber.ToLower().Contains(term))); }
        var sort = SortProperty(filter.SortBy, "CreatedAt", ("code", "Code"), ("name", "Name"), ("active", "IsActive"), ("status", "IsActive"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<MasterRecordResponse>> GetSupplierAccountAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.SupplierAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<MasterRecordResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetSupplierInvoicesAsync(PaginationRequest pagination, SupplierInvoiceListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.SupplierInvoices.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.SupplierAccountId.HasValue) query = query.Where(x => x.SupplierAccountId == filter.SupplierAccountId.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.InvoiceDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.InvoiceDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.InvoiceNumber.ToLower().Contains(term) || x.SupplierAccount.Code.ToLower().Contains(term) || x.SupplierAccount.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "InvoiceDate", ("number", "InvoiceNumber"), ("status", "Status"), ("amount", "GrossAmount"), ("duedate", "DueDate"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, x => ToResponse(x), ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetSupplierInvoiceAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var invoice = await dbcontext.SupplierInvoices.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (invoice is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, invoice.LegalEntityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var allocated = await dbcontext.SupplierPaymentAllocations.AsNoTracking().Where(x => x.SupplierInvoiceId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        return Result.Success(ToResponse(invoice, Math.Max(0m, invoice.GrossAmount - allocated)));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetSupplierPaymentsAsync(PaginationRequest pagination, SupplierPaymentListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.SupplierPayments.AsNoTracking().Include(x => x.Allocations).Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.SupplierAccountId.HasValue) query = query.Where(x => x.SupplierAccountId == filter.SupplierAccountId.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.PaymentDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.PaymentDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.PaymentNumber.ToLower().Contains(term) || x.ExternalReference.ToLower().Contains(term) || x.SupplierAccount.Code.ToLower().Contains(term) || x.SupplierAccount.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "PaymentDate", ("number", "PaymentNumber"), ("status", "Status"), ("amount", "Amount"), ("reference", "ExternalReference"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, x => ToResponse(x), ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetSupplierPaymentAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var payment = await dbcontext.SupplierPayments.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payment is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, payment.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FinancialOperationResponse>(access.Error) : Result.Success(ToResponse(payment));
    }

    public async Task<Result<PagedResponse<SourceEvidenceResponse>>> GetSourceEvidenceAsync(PaginationRequest pagination, SourceEvidenceListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<SourceEvidenceResponse>>(error);
        var query = dbcontext.SourceEvidences.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.PlatformAccountId.HasValue) query = query.Where(x => x.PlatformAccountId == filter.PlatformAccountId.Value);
        query = ApplyCreatedDateFilter(query, filter.FromDate, filter.ToDate, x => x.ReceivedAt);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.EvidenceType.ToLower().Contains(term) || x.ExternalReference.ToLower().Contains(term) || x.ContentHash.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "ReceivedAt", ("type", "EvidenceType"), ("evidencetype", "EvidenceType"), ("reference", "ExternalReference"), ("status", "Status"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<SourceEvidenceResponse>> GetSourceEvidenceAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.SourceEvidences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<SourceEvidenceResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetExpenseClaimsAsync(PaginationRequest pagination, ExpenseClaimListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.ExpenseClaims.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.EmployeeIqamaNo.HasValue) query = query.Where(x => x.EmployeeIqamaNo == filter.EmployeeIqamaNo.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.ClaimDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.ClaimDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.ClaimNumber.ToLower().Contains(term) || x.Description.ToLower().Contains(term) || x.EmployeeIqamaNo.ToString().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "ClaimDate", ("number", "ClaimNumber"), ("status", "Status"), ("amount", "NetAmount"), ("employeeiqamano", "EmployeeIqamaNo"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetExpenseClaimAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.ExpenseClaims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FinancialOperationResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<MasterRecordResponse>>> GetInventoryItemsAsync(PaginationRequest pagination, MasterRecordListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<MasterRecordResponse>>(error);
        var query = dbcontext.InventoryItems.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Active.HasValue) query = query.Where(x => x.IsActive == filter.Active.Value);
        query = ApplyCreatedDateFilter(query, filter.FromDate, filter.ToDate, x => x.CreatedAt);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Sku.ToLower().Contains(term) || x.Name.ToLower().Contains(term) || x.UnitOfMeasure.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "CreatedAt", ("sku", "Sku"), ("code", "Sku"), ("name", "Name"), ("unitofmeasure", "UnitOfMeasure"), ("active", "IsActive"), ("status", "IsActive"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<MasterRecordResponse>> GetInventoryItemAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.InventoryItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<MasterRecordResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetInventoryMovementsAsync(PaginationRequest pagination, InventoryMovementListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.InventoryMovements.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.MovementType == filter.Status.Value);
        if (filter.InventoryItemId.HasValue) query = query.Where(x => x.InventoryItemId == filter.InventoryItemId.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.MovementDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.MovementDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Reference.ToLower().Contains(term) || x.FromBin.ToLower().Contains(term) || x.ToBin.ToLower().Contains(term) || x.InventoryItem.Sku.ToLower().Contains(term) || x.InventoryItem.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "MovementDate", ("reference", "Reference"), ("status", "MovementType"), ("movementtype", "MovementType"), ("quantity", "Quantity"), ("unitcost", "UnitCost"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetInventoryMovementAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.InventoryMovements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FinancialOperationResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<InventoryStockBalanceResponse>>> GetInventoryStockBalancesAsync(PaginationRequest pagination, InventoryStockBalanceListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<InventoryStockBalanceResponse>>(error);
        var movements = dbcontext.InventoryMovements.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.InventoryItemId.HasValue) movements = movements.Where(x => x.InventoryItemId == filter.InventoryItemId.Value);
        if (filter.FromDate.HasValue) movements = movements.Where(x => x.MovementDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) movements = movements.Where(x => x.MovementDate <= filter.ToDate.Value);
        var incoming = movements.Where(x => (x.MovementType == InventoryMovementType.Receipt || x.MovementType == InventoryMovementType.Adjustment || x.MovementType == InventoryMovementType.Transfer) && x.ToBin != "")
            .Select(x => new { x.InventoryItemId, x.LegalEntityId, x.InventoryItem.Sku, ItemName = x.InventoryItem.Name, x.InventoryItem.UnitOfMeasure, Bin = x.ToBin, Quantity = x.Quantity, Value = x.Quantity * x.UnitCost });
        var outgoing = movements.Where(x => (x.MovementType == InventoryMovementType.Issue || x.MovementType == InventoryMovementType.Transfer) && x.FromBin != "")
            .Select(x => new { x.InventoryItemId, x.LegalEntityId, x.InventoryItem.Sku, ItemName = x.InventoryItem.Name, x.InventoryItem.UnitOfMeasure, Bin = x.FromBin, Quantity = -x.Quantity, Value = -(x.Quantity * x.UnitCost) });
        // Keep filtering and the two signed projections in SQL, then aggregate the
        // compact movement legs in memory. EF providers cannot consistently
        // translate the Concat + GroupBy + record-constructor projection.
        var legs = await incoming.Concat(outgoing).ToListAsync(ct);
        IEnumerable<InventoryStockBalanceResponse> grouped = legs
            .GroupBy(x => new { x.InventoryItemId, x.LegalEntityId, x.Sku, x.ItemName, x.UnitOfMeasure, x.Bin })
            .Select(x => new InventoryStockBalanceResponse(x.Key.InventoryItemId, x.Key.LegalEntityId, x.Key.Sku, x.Key.ItemName, x.Key.UnitOfMeasure, x.Key.Bin, x.Sum(v => v.Quantity), x.Sum(v => v.Value)));
        if (!string.IsNullOrWhiteSpace(filter.Bin)) { var bin = filter.Bin.Trim(); grouped = grouped.Where(x => string.Equals(x.Bin, bin, StringComparison.OrdinalIgnoreCase)); }
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = filter.Search.Trim(); grouped = grouped.Where(x => x.Sku.Contains(term, StringComparison.OrdinalIgnoreCase) || x.ItemName.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Bin.Contains(term, StringComparison.OrdinalIgnoreCase)); }
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize; var total = grouped.Count(); var desc = IsDescending(filter.SortDirection);
        IOrderedEnumerable<InventoryStockBalanceResponse> ordered = (filter.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "itemname" or "name" => desc ? grouped.OrderByDescending(x => x.ItemName).ThenByDescending(x => x.InventoryItemId).ThenByDescending(x => x.Bin) : grouped.OrderBy(x => x.ItemName).ThenBy(x => x.InventoryItemId).ThenBy(x => x.Bin),
            "bin" => desc ? grouped.OrderByDescending(x => x.Bin).ThenByDescending(x => x.InventoryItemId) : grouped.OrderBy(x => x.Bin).ThenBy(x => x.InventoryItemId),
            "quantity" => desc ? grouped.OrderByDescending(x => x.Quantity).ThenByDescending(x => x.InventoryItemId).ThenByDescending(x => x.Bin) : grouped.OrderBy(x => x.Quantity).ThenBy(x => x.InventoryItemId).ThenBy(x => x.Bin),
            "value" => desc ? grouped.OrderByDescending(x => x.Value).ThenByDescending(x => x.InventoryItemId).ThenByDescending(x => x.Bin) : grouped.OrderBy(x => x.Value).ThenBy(x => x.InventoryItemId).ThenBy(x => x.Bin),
            _ => desc ? grouped.OrderByDescending(x => x.Sku).ThenByDescending(x => x.InventoryItemId).ThenByDescending(x => x.Bin) : grouped.OrderBy(x => x.Sku).ThenBy(x => x.InventoryItemId).ThenBy(x => x.Bin)
        };
        var items = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return Result.Success(new PagedResponse<InventoryStockBalanceResponse>(items, pageNumber, pageSize, total));
    }

    public async Task<Result<PagedResponse<MasterRecordResponse>>> GetBankAccountsAsync(PaginationRequest pagination, MasterRecordListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<MasterRecordResponse>>(error);
        var query = dbcontext.BankAccounts.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Active.HasValue) query = query.Where(x => x.IsActive == filter.Active.Value);
        query = ApplyCreatedDateFilter(query, filter.FromDate, filter.ToDate, x => x.CreatedAt);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term) || x.CurrencyCode.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "CreatedAt", ("code", "Code"), ("name", "Name"), ("currency", "CurrencyCode"), ("currencycode", "CurrencyCode"), ("active", "IsActive"), ("status", "IsActive"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<MasterRecordResponse>> GetBankAccountAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<MasterRecordResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<FinancialOperationResponse>>> GetBankStatementLinesAsync(PaginationRequest pagination, BankStatementLineListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FinancialOperationResponse>>(error);
        var query = dbcontext.BankStatementLines.AsNoTracking().Include(x => x.BankAccount).Where(x => x.BankAccount.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.BankAccountId.HasValue) query = query.Where(x => x.BankAccountId == filter.BankAccountId.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.TransactionDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.TransactionDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.ExternalReference.ToLower().Contains(term) || x.Description.ToLower().Contains(term) || x.BankAccount.Code.ToLower().Contains(term) || x.BankAccount.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "TransactionDate", ("reference", "ExternalReference"), ("status", "Status"), ("amount", "Amount"), ("description", "Description"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<FinancialOperationResponse>> GetBankStatementLineAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.BankStatementLines.AsNoTracking().Include(x => x.BankAccount).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.BankAccount.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FinancialOperationResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<TaxCodeResponse>>> GetTaxCodesAsync(PaginationRequest pagination, TaxCodeListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<TaxCodeResponse>>(error);
        var query = dbcontext.TaxCodes.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Active.HasValue) query = query.Where(x => x.IsActive == filter.Active.Value);
        if (filter.Direction.HasValue) query = query.Where(x => x.Direction == filter.Direction.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.EffectiveTo == null || x.EffectiveTo >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.EffectiveFrom <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "EffectiveFrom", ("code", "Code"), ("name", "Name"), ("direction", "Direction"), ("rate", "Rate"), ("active", "IsActive"), ("status", "IsActive"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<TaxCodeResponse>> GetTaxCodeAsync(int id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.TaxCodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<TaxCodeResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<TaxCodeResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<TaxReturnResponse>>> GetTaxReturnsAsync(PaginationRequest pagination, TaxReturnListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<TaxReturnResponse>>(error);
        var query = dbcontext.TaxReturns.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.PeriodEnd >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.PeriodStart <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => (x.SubmissionReference != null && x.SubmissionReference.ToLower().Contains(term)) || x.CreatedBy.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "PeriodStart", ("status", "Status"), ("periodend", "PeriodEnd"), ("amount", "NetTaxPayableAmount"), ("submissionreference", "SubmissionReference"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, x => ToResponse(x), ct));
    }

    public async Task<Result<TaxReturnResponse>> GetTaxReturnAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.TaxReturns.AsNoTracking().Include(x => x.TaxTransactions).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<TaxReturnResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<TaxReturnResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<FixedAssetResponse>>> GetFixedAssetsAsync(PaginationRequest pagination, FixedAssetListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<FixedAssetResponse>>(error);
        var query = dbcontext.FixedAssets.AsNoTracking().Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.AcquisitionDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.AcquisitionDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.AssetNumber.ToLower().Contains(term) || x.Description.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "AcquisitionDate", ("number", "AssetNumber"), ("assetnumber", "AssetNumber"), ("status", "Status"), ("amount", "AcquisitionCost"), ("description", "Description"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<FixedAssetResponse>> GetFixedAssetAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.FixedAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<FixedAssetResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<FixedAssetResponse>(access.Error) : Result.Success(ToResponse(item));
    }

    public async Task<Result<PagedResponse<BudgetResponse>>> GetBudgetsAsync(PaginationRequest pagination, BudgetListFilter filter, string actorId, CancellationToken ct = default)
    {
        var error = await ValidateRegisterAsync(filter, actorId, ct); if (error is not null) return Result.Failure<PagedResponse<BudgetResponse>>(error);
        var query = dbcontext.Budgets.AsNoTracking().Include(x => x.Lines).Where(x => x.LegalEntityId == filter.LegalEntityId);
        if (filter.Approved.HasValue) query = query.Where(x => x.IsApproved == filter.Approved.Value);
        if (filter.FromDate.HasValue) query = query.Where(x => x.EndDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.StartDate <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = Search(filter.Search); query = query.Where(x => x.Name.ToLower().Contains(term)); }
        var sort = SortProperty(filter.SortBy, "StartDate", ("name", "Name"), ("enddate", "EndDate"), ("approved", "IsApproved"), ("status", "IsApproved"));
        return Result.Success(await PageAsync(query, pagination, sort, filter.SortDirection, x => x.Id, ToResponse, ct));
    }

    public async Task<Result<BudgetResponse>> GetBudgetAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var item = await dbcontext.Budgets.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result.Failure<BudgetResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, item.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<BudgetResponse>(access.Error) : Result.Success(ToResponse(item));
    }




    public async Task<Result<SourceEvidenceResponse>> CreateSourceEvidenceAsync(CreateSourceEvidenceRequest r, string actorId, CancellationToken ct = default)
    {
        // Compatibility endpoint: the old field name remains for one release, but
        // physical paths are no longer accepted. It must contain a private file ID.
        if (!Guid.TryParse(r.StorageLocator, out var storedFileId))
            return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.InvalidRequest);
        return await CreatePrivateSourceEvidenceAsync(new CreatePrivateSourceEvidenceRequest(
            r.LegalEntityId, r.PlatformAccountId, r.EvidenceType, r.ExternalReference, storedFileId, r.MetadataJson), actorId, ct);
    }

    public async Task<Result<SourceEvidenceResponse>> CreatePrivateSourceEvidenceAsync(CreatePrivateSourceEvidenceRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<SourceEvidenceResponse>(access.Error);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct) ||
            (r.PlatformAccountId is not null && !await dbcontext.PlatformAccounts.AnyAsync(x => x.Id == r.PlatformAccountId && x.LegalEntityId == r.LegalEntityId, ct)))
            return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.NotFound);
        var file = await dbcontext.AccountingStoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.StoredFileId && x.LegalEntityId == r.LegalEntityId && x.Status == StoredFileStatus.Active, ct);
        if (file is null) return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.NotFound);
        if (await dbcontext.SourceEvidences.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.ContentHash == file.Sha256, ct))
            return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.Duplicate);
        try { JsonDocument.Parse(r.MetadataJson); } catch (JsonException) { return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.InvalidRequest); }
        var evidence = new SourceEvidence
        {
            LegalEntityId = r.LegalEntityId, PlatformAccountId = r.PlatformAccountId, StoredFileId = file.Id,
            EvidenceType = r.EvidenceType.Trim(), ExternalReference = r.ExternalReference.Trim(),
            // Retained only for compatibility with the legacy schema; it is never returned by an API.
            StorageLocator = file.StorageLocator, ContentHash = file.Sha256, MetadataJson = r.MetadataJson, ReceivedBy = actorId
        };
        dbcontext.SourceEvidences.Add(evidence);
        await AuditAsync(r.LegalEntityId, "Evidence.Received", actorId, new { evidence.Id, evidence.StoredFileId, evidence.EvidenceType, evidence.ExternalReference, evidence.ContentHash }, ct);
        await dbcontext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(evidence));
    }

    public async Task<Result<SourceEvidenceResponse>> ReviewSourceEvidenceAsync(Guid id, ReviewSourceEvidenceRequest r, string actorId, CancellationToken ct = default)
    {
        var evidence = await dbcontext.SourceEvidences.SingleOrDefaultAsync(x => x.Id == id, ct); if (evidence is null) return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, evidence.LegalEntityId, FinancialPermission.Approve, ct); if (access.IsFailure) return Result.Failure<SourceEvidenceResponse>(access.Error);
        if (evidence.Status != SourceEvidenceStatus.Received) return Result.Failure<SourceEvidenceResponse>(FinancialOperationsErrors.InvalidState);
        evidence.Status = r.Accept ? SourceEvidenceStatus.Accepted : SourceEvidenceStatus.Rejected; evidence.ReviewedBy = actorId; evidence.ReviewedAt = DateTime.UtcNow; evidence.ReviewComment = Trim(r.Comment);
        await AuditAsync(evidence.LegalEntityId, r.Accept ? "Evidence.Accepted" : "Evidence.Rejected", actorId, new { evidence.Id, evidence.ContentHash }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(evidence));
    }

    public async Task<Result<FinancialOperationResponse>> RecordPlatformSettlementAsync(RecordPlatformSettlementRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var evidence = await dbcontext.SourceEvidences.SingleOrDefaultAsync(x => x.Id == r.SourceEvidenceId && x.LegalEntityId == r.LegalEntityId, ct); if (evidence is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound); if (evidence.Status != SourceEvidenceStatus.Accepted) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.EvidenceNotAccepted);
        if (r.GrossRevenue <= 0 || r.CommissionAmount < 0 || r.NetSettlementAmount < 0 || Round(r.GrossRevenue - r.CommissionAmount) != Round(r.NetSettlementAmount)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var clearingAccountId = r.PlatformClearingAccountId;
        var commissionAccountId = r.CommissionExpenseAccountId;
        var revenueAccountId = r.RevenueAccountId;
        if (clearingAccountId <= 0 && commissionAccountId <= 0 && revenueAccountId <= 0)
        {
            var netRoute = r.NetSettlementAmount > 0 ? await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.SettlementDate, "PLATFORM_SETTLEMENT_NET", ct) : null;
            var commissionRoute = r.CommissionAmount > 0 ? await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.SettlementDate, "PLATFORM_COMMISSION", ct) : null;
            clearingAccountId = netRoute?.DebitAccountId ?? commissionRoute?.CreditAccountId ?? 0;
            revenueAccountId = netRoute?.CreditAccountId ?? 0;
            commissionAccountId = commissionRoute?.DebitAccountId ?? clearingAccountId;
            if (clearingAccountId <= 0 || revenueAccountId <= 0 || (r.CommissionAmount > 0 && commissionRoute is null)) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
        }
        else if (!await AccountsExistAsync(r.LegalEntityId, [clearingAccountId, commissionAccountId, revenueAccountId], ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var settlement = new PlatformSettlement { LegalEntityId = r.LegalEntityId, SourceEvidenceId = r.SourceEvidenceId, SettlementReference = r.SettlementReference.Trim(), SettlementDate = r.SettlementDate, GrossRevenue = Round(r.GrossRevenue), CommissionAmount = Round(r.CommissionAmount), NetSettlementAmount = Round(r.NetSettlementAmount), PlatformClearingAccountId = clearingAccountId, CommissionExpenseAccountId = commissionAccountId, RevenueAccountId = revenueAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId };
        var events = new List<PostingEventAmount>();
        if (settlement.NetSettlementAmount > 0) events.Add(new("PLATFORM_SETTLEMENT_NET", settlement.NetSettlementAmount, $"Net settlement {settlement.SettlementReference}"));
        if (settlement.CommissionAmount > 0) events.Add(new("PLATFORM_COMMISSION", settlement.CommissionAmount, $"Platform commission {settlement.SettlementReference}"));
        var command = new PostSourceDocumentRequest(r.LegalEntityId, null, r.SettlementDate, "PlatformSettlement", SourceReference("SET", settlement.SettlementReference), settlement.PostingProfileCode, $"Platform settlement {settlement.SettlementReference}", "SAR", r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Receivables, events,
            CanonicalPayload(new { r.LegalEntityId, r.SourceEvidenceId, SettlementReference = settlement.SettlementReference, r.SettlementDate, r.GrossRevenue, r.CommissionAmount, r.NetSettlementAmount, r.PlatformClearingAccountId, r.CommissionExpenseAccountId, r.RevenueAccountId, PostingProfileCode = Code(r.PostingProfileCode) }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            var original = await dbcontext.PlatformSettlements.AsNoTracking().SingleOrDefaultAsync(x => x.FinancialDocumentId == replay.Value.Id, ct);
            return original is null ? Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState) : Result.Success(ToResponse(original));
        }
        if (await dbcontext.PlatformSettlements.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.SettlementReference == settlement.SettlementReference, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            settlement.Status = SettlementStatus.Recorded; settlement.FinancialDocumentId = id; dbcontext.PlatformSettlements.Add(settlement);
        }, "PlatformSettlement.Recorded", id => new { SettlementId = settlement.Id, settlement.SettlementReference, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(settlement));
    }

    public async Task<Result<MasterRecordResponse>> CreateCustomerAccountAsync(CreateCustomerAccountRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<MasterRecordResponse>(access.Error);
        var code = Code(r.Code); if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound); if (await dbcontext.CustomerAccounts.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == code, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.Duplicate);
        var customer = new CustomerAccount { LegalEntityId = r.LegalEntityId, Code = code, Name = r.Name.Trim(), TaxRegistrationNumber = Trim(r.TaxRegistrationNumber) }; dbcontext.CustomerAccounts.Add(customer); await AuditAsync(r.LegalEntityId, "Customer.Created", actorId, new { customer.Id, customer.Code }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(customer));
    }

    public async Task<Result<FinancialOperationResponse>> CreateCustomerInvoiceAsync(CreateCustomerInvoiceRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        if (r.DueDate < r.InvoiceDate || r.Lines.Count == 0 || await dbcontext.CustomerInvoices.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.InvoiceNumber == r.InvoiceNumber.Trim(), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        if (!await dbcontext.CustomerAccounts.AnyAsync(x => x.Id == r.CustomerAccountId && x.LegalEntityId == r.LegalEntityId && x.IsActive, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var receivableAccountId = r.ReceivableAccountId;
        var revenueAccountIds = r.Lines.Select(x => x.RevenueAccountId).ToArray();
        if (receivableAccountId <= 0 && revenueAccountIds.All(x => x <= 0))
        {
            var route = await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.InvoiceDate, "AR_REVENUE", ct);
            if (route is null) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
            receivableAccountId = route.DebitAccountId;
            revenueAccountIds = Enumerable.Repeat(route.CreditAccountId, r.Lines.Count).ToArray();
        }
        else if (receivableAccountId <= 0 || revenueAccountIds.Any(x => x <= 0) || !await AccountsExistAsync(r.LegalEntityId, revenueAccountIds.Append(receivableAccountId), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        if (r.SourceEvidenceId is not null && !await EvidenceAcceptedAsync(r.SourceEvidenceId.Value, r.LegalEntityId, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.EvidenceNotAccepted);
        var taxes = await LoadTaxesAsync(r.LegalEntityId, r.Lines.Select(x => x.TaxCodeId), TaxDirection.Output, r.InvoiceDate, ct); if (taxes is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var invoice = new CustomerInvoice { LegalEntityId = r.LegalEntityId, CustomerAccountId = r.CustomerAccountId, SourceEvidenceId = r.SourceEvidenceId, InvoiceNumber = r.InvoiceNumber.Trim(), InvoiceDate = r.InvoiceDate, DueDate = r.DueDate, CurrencyCode = Code(r.CurrencyCode), ExchangeRate = r.ExchangeRate, ReceivableAccountId = receivableAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId };
        invoice.Lines = r.Lines.Select((line, index) => { var net = Round(line.Quantity * line.UnitPrice); var tax = line.TaxCodeId is null ? 0 : Round(net * taxes[line.TaxCodeId.Value].Rate); return new CustomerInvoiceLine { LineNumber = index + 1, Description = line.Description.Trim(), Quantity = line.Quantity, UnitPrice = line.UnitPrice, RevenueAccountId = revenueAccountIds[index], TaxCodeId = line.TaxCodeId, NetAmount = net, TaxAmount = tax }; }).ToList();
        invoice.NetAmount = invoice.Lines.Sum(x => x.NetAmount); invoice.TaxAmount = invoice.Lines.Sum(x => x.TaxAmount); invoice.GrossAmount = invoice.NetAmount + invoice.TaxAmount; if (invoice.GrossAmount <= 0) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        dbcontext.CustomerInvoices.Add(invoice); await AuditAsync(r.LegalEntityId, "CustomerInvoice.Created", actorId, new { invoice.Id, invoice.InvoiceNumber, invoice.GrossAmount }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(invoice));
    }

    public async Task<Result<FinancialOperationResponse>> IssueCustomerInvoiceAsync(Guid id, IssueCustomerInvoiceRequest r, string actorId, CancellationToken ct = default)
    {
        var invoice = await dbcontext.CustomerInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct); if (invoice is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var access = await RequireAsync(actorId, invoice.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var events = new List<PostingEventAmount>();
        if (invoice.NetAmount > 0) events.Add(new("AR_REVENUE", invoice.NetAmount, $"Revenue for {invoice.InvoiceNumber}"));
        if (invoice.TaxAmount > 0) events.Add(new("AR_OUTPUT_VAT", invoice.TaxAmount, $"Output VAT for {invoice.InvoiceNumber}"));
        var command = new PostSourceDocumentRequest(invoice.LegalEntityId, null, invoice.InvoiceDate, "CustomerInvoice", $"AR:{invoice.Id:N}", invoice.PostingProfileCode, $"Customer invoice {invoice.InvoiceNumber}", invoice.CurrencyCode, r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Receivables, events,
            CanonicalPayload(new { InvoiceId = invoice.Id }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            return Result.Success(ToResponse(invoice));
        }
        if (invoice.Status != ReceivableInvoiceStatus.Draft) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            invoice.Status = ReceivableInvoiceStatus.Issued; invoice.FinancialDocumentId = id;
            foreach (var line in invoice.Lines.Where(x => x.TaxAmount > 0)) dbcontext.TaxTransactions.Add(new TaxTransaction { LegalEntityId = invoice.LegalEntityId, TaxCodeId = line.TaxCodeId!.Value, FinancialDocumentId = id, SourceReference = $"AR:{invoice.Id:N}", TransactionDate = invoice.InvoiceDate, NetAmount = line.NetAmount, TaxAmount = line.TaxAmount, Direction = TaxDirection.Output });
        }, "CustomerInvoice.Issued", id => new { InvoiceId = invoice.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(invoice));
    }

    public async Task<Result<FinancialOperationResponse>> RecordCustomerReceiptAsync(RecordCustomerReceiptRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        if (r.Amount <= 0 || !await dbcontext.CustomerAccounts.AnyAsync(x => x.Id == r.CustomerAccountId && x.LegalEntityId == r.LegalEntityId && x.IsActive, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var cashAccountId = r.CashAccountId; var receiptReceivableAccountId = r.ReceivableAccountId;
        if (cashAccountId <= 0 && receiptReceivableAccountId <= 0)
        {
            var route = await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.ReceiptDate, "AR_RECEIPT", ct);
            if (route is null) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
            cashAccountId = route.DebitAccountId; receiptReceivableAccountId = route.CreditAccountId;
        }
        else if (cashAccountId <= 0 || receiptReceivableAccountId <= 0 || !await AccountsExistAsync(r.LegalEntityId, [cashAccountId, receiptReceivableAccountId], ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var receipt = new CustomerReceipt { LegalEntityId = r.LegalEntityId, CustomerAccountId = r.CustomerAccountId, ReceiptNumber = r.ReceiptNumber.Trim(), ExternalReference = r.ExternalReference.Trim(), ReceiptDate = r.ReceiptDate, CurrencyCode = Code(r.CurrencyCode), ExchangeRate = r.ExchangeRate, Amount = Round(r.Amount), CashAccountId = cashAccountId, ReceivableAccountId = receiptReceivableAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId };
        var command = new PostSourceDocumentRequest(r.LegalEntityId, null, r.ReceiptDate, "CustomerReceipt", SourceReference("RCPT", receipt.ReceiptNumber), receipt.PostingProfileCode, $"Customer receipt {receipt.ReceiptNumber}", receipt.CurrencyCode, r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Receivables, [new PostingEventAmount("AR_RECEIPT", receipt.Amount, receipt.ReceiptNumber)],
            CanonicalPayload(new { r.LegalEntityId, r.CustomerAccountId, ReceiptNumber = receipt.ReceiptNumber, ExternalReference = receipt.ExternalReference, r.ReceiptDate, CurrencyCode = receipt.CurrencyCode, r.ExchangeRate, r.Amount, r.CashAccountId, r.ReceivableAccountId, PostingProfileCode = receipt.PostingProfileCode }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            var original = await dbcontext.CustomerReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FinancialDocumentId == replay.Value.Id, ct);
            return original is null ? Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState) : Result.Success(ToResponse(original));
        }
        if (await dbcontext.CustomerReceipts.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && (x.ReceiptNumber == receipt.ReceiptNumber || x.ExternalReference == receipt.ExternalReference), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            receipt.FinancialDocumentId = id; dbcontext.CustomerReceipts.Add(receipt);
        }, "CustomerReceipt.Recorded", id => new { ReceiptId = receipt.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(receipt));
    }

    public async Task<Result> AllocateCustomerReceiptAsync(Guid receiptId, AllocateCustomerReceiptRequest r, string actorId, CancellationToken ct = default)
    {
        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational()) transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (dbcontext.Database.IsSqlServer()) await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:ARAllocation:" + receiptId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
            var receipt = await dbcontext.CustomerReceipts.Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == receiptId, ct); if (receipt is null) return Result.Failure(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, receipt.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return access;
            var invoice = await dbcontext.CustomerInvoices.SingleOrDefaultAsync(x => x.Id == r.CustomerInvoiceId && x.LegalEntityId == receipt.LegalEntityId && x.CustomerAccountId == receipt.CustomerAccountId, ct); if (invoice is null) return Result.Failure(FinancialOperationsErrors.NotFound); if (r.Amount <= 0 || invoice.Status is ReceivableInvoiceStatus.Draft or ReceivableInvoiceStatus.Cancelled) return Result.Failure(FinancialOperationsErrors.InvalidState);
            var amount = Round(r.Amount); var receiptRemaining = receipt.Amount - receipt.Allocations.Sum(x => x.Amount); var invoiceApplied = await dbcontext.CustomerReceiptAllocations.Where(x => x.CustomerInvoiceId == invoice.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m; if (amount > receiptRemaining || amount > invoice.GrossAmount - invoiceApplied) return Result.Failure(FinancialOperationsErrors.AllocationExceedsBalance);
            receipt.Allocations.Add(new CustomerReceiptAllocation { CustomerInvoiceId = invoice.Id, Amount = amount, AllocatedBy = actorId }); invoiceApplied += amount; receipt.Status = receiptRemaining == amount ? ReceiptStatus.Applied : ReceiptStatus.PartiallyApplied; invoice.Status = invoiceApplied == invoice.GrossAmount ? ReceivableInvoiceStatus.Settled : ReceivableInvoiceStatus.PartiallySettled;
            await AuditAsync(receipt.LegalEntityId, "CustomerReceipt.Allocated", actorId, new { ReceiptId = receipt.Id, InvoiceId = invoice.Id, Amount = amount }, ct); await dbcontext.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return Result.Success();
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    public async Task<Result<MasterRecordResponse>> CreateEmployeePayContractAsync(CreateEmployeePayContractRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<MasterRecordResponse>(access.Error); if (r.GrossSalary < 0 || r.FixedDeduction < 0 || r.FixedDeduction > r.GrossSalary || r.EffectiveTo < r.EffectiveFrom || !await dbcontext.Employees.AnyAsync(x => x.IqamaNo == r.EmployeeIqamaNo && !x.IsDeleted, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.InvalidRequest);
        var overlap = await dbcontext.EmployeePayContracts.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.EmployeeIqamaNo == r.EmployeeIqamaNo && x.IsActive && x.EffectiveFrom <= (r.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo >= r.EffectiveFrom), ct); if (overlap) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.Duplicate);
        var contract = new EmployeePayContract { LegalEntityId = r.LegalEntityId, EmployeeIqamaNo = r.EmployeeIqamaNo, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, GrossSalary = Round(r.GrossSalary), FixedDeduction = Round(r.FixedDeduction), CurrencyCode = Code(r.CurrencyCode), CreatedBy = actorId }; dbcontext.EmployeePayContracts.Add(contract); await AuditAsync(r.LegalEntityId, "Payroll.ContractCreated", actorId, new { contract.Id, contract.EmployeeIqamaNo }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(new MasterRecordResponse(contract.Id, contract.LegalEntityId, contract.EmployeeIqamaNo.ToString(), "Employee payroll contract", contract.IsActive));
    }

    public async Task<Result<PayrollRunResponse>> CreatePayrollRunAsync(CreatePayrollRunRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<PayrollRunResponse>(access.Error); if (r.PeriodEnd < r.PeriodStart || await dbcontext.PayrollRuns.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && (x.RunNumber == r.RunNumber.Trim() || (x.PeriodStart == r.PeriodStart && x.PeriodEnd == r.PeriodEnd)), ct) || !await AccountsExistAsync(r.LegalEntityId, [r.PayrollExpenseAccountId, r.PayrollPayableAccountId, r.DeductionLiabilityAccountId], ct)) return Result.Failure<PayrollRunResponse>(FinancialOperationsErrors.InvalidRequest);
        var contracts = await dbcontext.EmployeePayContracts.Where(x => x.LegalEntityId == r.LegalEntityId && x.IsActive && x.EffectiveFrom <= r.PeriodEnd && (x.EffectiveTo == null || x.EffectiveTo >= r.PeriodEnd) && x.CurrencyCode == Code(r.CurrencyCode)).ToListAsync(ct); if (contracts.Count == 0) return Result.Failure<PayrollRunResponse>(FinancialOperationsErrors.InvalidRequest);
        var run = new PayrollRun { LegalEntityId = r.LegalEntityId, RunNumber = r.RunNumber.Trim(), PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd, CurrencyCode = Code(r.CurrencyCode), PayrollExpenseAccountId = r.PayrollExpenseAccountId, PayrollPayableAccountId = r.PayrollPayableAccountId, DeductionLiabilityAccountId = r.DeductionLiabilityAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId, Lines = contracts.Select(x => new PayrollRunLine { EmployeeIqamaNo = x.EmployeeIqamaNo, EmployeePayContractId = x.Id, GrossAmount = x.GrossSalary, DeductionAmount = x.FixedDeduction, NetAmount = x.GrossSalary - x.FixedDeduction }).ToList() };
        run.GrossAmount = run.Lines.Sum(x => x.GrossAmount); run.DeductionAmount = run.Lines.Sum(x => x.DeductionAmount); run.NetAmount = run.Lines.Sum(x => x.NetAmount); dbcontext.PayrollRuns.Add(run); await AuditAsync(r.LegalEntityId, "Payroll.RunCreated", actorId, new { run.Id, run.RunNumber, run.NetAmount }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(run));
    }

    public async Task<Result<PayrollRunResponse>> PreparePayrollRunAsync(Guid id, PreparePayrollRunRequest r, string actorId, CancellationToken ct = default)
    {
        var run = await dbcontext.PayrollRuns.SingleOrDefaultAsync(x => x.Id == id, ct); if (run is null) return Result.Failure<PayrollRunResponse>(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, run.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<PayrollRunResponse>(access.Error);
        var events = new List<PostingEventAmount>();
        if (run.NetAmount > 0) events.Add(new("LEGACY_PAYROLL_NET", run.NetAmount, run.RunNumber));
        if (run.DeductionAmount > 0) events.Add(new("LEGACY_PAYROLL_DEDUCTION", run.DeductionAmount, $"Deductions {run.RunNumber}"));
        var command = new PostSourceDocumentRequest(run.LegalEntityId, null, run.PeriodEnd, "LegacyPayrollAccrual", $"PAY:{run.Id:N}", run.PostingProfileCode, $"Payroll accrual {run.RunNumber}", run.CurrencyCode, r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Payroll, events,
            CanonicalPayload(new { PayrollRunId = run.Id }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<PayrollRunResponse>(replay.Error);
            return Result.Success(ToResponse(run));
        }
        if (run.Status != PayrollRunStatus.Draft) return Result.Failure<PayrollRunResponse>(FinancialOperationsErrors.InvalidState);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            run.Status = PayrollRunStatus.Prepared; run.AccrualFinancialDocumentId = id;
        }, "Payroll.RunPrepared", id => new { PayrollRunId = run.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<PayrollRunResponse>(document.Error);
        return Result.Success(ToResponse(run));
    }

    public async Task<Result<PayrollRunResponse>> PayPayrollRunAsync(Guid id, PayPayrollRunRequest r, string actorId, CancellationToken ct = default)
    {
        var run = await dbcontext.PayrollRuns.SingleOrDefaultAsync(x => x.Id == id, ct); if (run is null) return Result.Failure<PayrollRunResponse>(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, run.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<PayrollRunResponse>(access.Error);
        var command = new PostSourceDocumentRequest(run.LegalEntityId, null, r.PaymentDate, "LegacyPayrollPayment", $"PAYMENT:{run.Id:N}", Code(r.PostingProfileCode), $"Payroll payment {run.RunNumber}", run.CurrencyCode, r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Payroll, [new PostingEventAmount("LEGACY_PAYROLL_PAYMENT", run.NetAmount, run.RunNumber)],
            CanonicalPayload(new { PayrollRunId = run.Id, r.PaymentDate, r.CashAccountId, PostingProfileCode = Code(r.PostingProfileCode) }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<PayrollRunResponse>(replay.Error);
            return Result.Success(ToResponse(run));
        }
        if (run.Status != PayrollRunStatus.Prepared || !await AccountsExistAsync(run.LegalEntityId, [run.PayrollPayableAccountId, r.CashAccountId], ct)) return Result.Failure<PayrollRunResponse>(FinancialOperationsErrors.InvalidState);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            run.Status = PayrollRunStatus.Paid; run.PaymentFinancialDocumentId = id;
        }, "Payroll.RunPaid", id => new { PayrollRunId = run.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<PayrollRunResponse>(document.Error);
        return Result.Success(ToResponse(run));
    }

    public async Task<Result<MasterRecordResponse>> CreateSupplierAccountAsync(CreateSupplierAccountRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<MasterRecordResponse>(access.Error); var code = Code(r.Code);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound); if (await dbcontext.SupplierAccounts.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == code, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.Duplicate);
        var supplier = new SupplierAccount { LegalEntityId = r.LegalEntityId, Code = code, Name = r.Name.Trim(), TaxRegistrationNumber = Trim(r.TaxRegistrationNumber) }; dbcontext.SupplierAccounts.Add(supplier); await AuditAsync(r.LegalEntityId, "Supplier.Created", actorId, new { supplier.Id, supplier.Code }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(supplier));
    }

    public async Task<Result<FinancialOperationResponse>> CreateSupplierInvoiceAsync(CreateSupplierInvoiceRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        if (r.DueDate < r.InvoiceDate || r.Lines.Count == 0 || await dbcontext.SupplierInvoices.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.InvoiceNumber == r.InvoiceNumber.Trim(), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        if (!await dbcontext.SupplierAccounts.AnyAsync(x => x.Id == r.SupplierAccountId && x.LegalEntityId == r.LegalEntityId && x.IsActive, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound);
        var payableAccountId = r.PayableAccountId;
        var expenseAccountIds = r.Lines.Select(x => x.ExpenseOrInventoryAccountId).ToArray();
        if (payableAccountId <= 0 && expenseAccountIds.All(x => x <= 0))
        {
            var route = await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.InvoiceDate, "AP_INVOICE_NET", ct);
            if (route is null) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
            expenseAccountIds = Enumerable.Repeat(route.DebitAccountId, r.Lines.Count).ToArray();
            payableAccountId = route.CreditAccountId;
        }
        else if (payableAccountId <= 0 || expenseAccountIds.Any(x => x <= 0) || !await AccountsExistAsync(r.LegalEntityId, expenseAccountIds.Append(payableAccountId), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        if (r.SourceEvidenceId is not null && !await EvidenceAcceptedAsync(r.SourceEvidenceId.Value, r.LegalEntityId, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.EvidenceNotAccepted);
        var taxes = await LoadTaxesAsync(r.LegalEntityId, r.Lines.Select(x => x.TaxCodeId), TaxDirection.Input, r.InvoiceDate, ct); if (taxes is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var invoice = new SupplierInvoice { LegalEntityId = r.LegalEntityId, SupplierAccountId = r.SupplierAccountId, SourceEvidenceId = r.SourceEvidenceId, InvoiceNumber = r.InvoiceNumber.Trim(), InvoiceDate = r.InvoiceDate, DueDate = r.DueDate, CurrencyCode = Code(r.CurrencyCode), ExchangeRate = r.ExchangeRate, PayableAccountId = payableAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId };
        invoice.Lines = r.Lines.Select((line, index) => { var net = Round(line.Quantity * line.UnitPrice); var tax = line.TaxCodeId is null ? 0 : Round(net * taxes[line.TaxCodeId.Value].Rate); return new SupplierInvoiceLine { LineNumber = index + 1, Description = line.Description.Trim(), Quantity = line.Quantity, UnitPrice = line.UnitPrice, ExpenseOrInventoryAccountId = expenseAccountIds[index], TaxCodeId = line.TaxCodeId, NetAmount = net, TaxAmount = tax }; }).ToList(); invoice.NetAmount = invoice.Lines.Sum(x => x.NetAmount); invoice.TaxAmount = invoice.Lines.Sum(x => x.TaxAmount); invoice.GrossAmount = invoice.NetAmount + invoice.TaxAmount; if (invoice.GrossAmount <= 0) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        dbcontext.SupplierInvoices.Add(invoice); await AuditAsync(r.LegalEntityId, "SupplierInvoice.Created", actorId, new { invoice.Id, invoice.InvoiceNumber, invoice.GrossAmount }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(invoice));
    }

    public async Task<Result<FinancialOperationResponse>> RecordSupplierInvoiceAsync(Guid id, RecordSupplierInvoiceRequest r, string actorId, CancellationToken ct = default)
    {
        var invoice = await dbcontext.SupplierInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct); if (invoice is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, invoice.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var events = new List<PostingEventAmount>();
        if (invoice.NetAmount > 0) events.Add(new("AP_INVOICE_NET", invoice.NetAmount, $"Supplier invoice {invoice.InvoiceNumber}"));
        if (invoice.TaxAmount > 0) events.Add(new("AP_INPUT_VAT", invoice.TaxAmount, $"Input VAT {invoice.InvoiceNumber}"));
        var command = new PostSourceDocumentRequest(invoice.LegalEntityId, null, invoice.InvoiceDate, "SupplierInvoice", $"AP:{invoice.Id:N}", invoice.PostingProfileCode, $"Supplier invoice {invoice.InvoiceNumber}", invoice.CurrencyCode, r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Payables, events,
            CanonicalPayload(new { InvoiceId = invoice.Id }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            return Result.Success(ToResponse(invoice));
        }
        if (invoice.Status != PayableInvoiceStatus.Draft) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            invoice.Status = PayableInvoiceStatus.Recorded; invoice.FinancialDocumentId = id;
            foreach (var line in invoice.Lines.Where(x => x.TaxAmount > 0)) dbcontext.TaxTransactions.Add(new TaxTransaction { LegalEntityId = invoice.LegalEntityId, TaxCodeId = line.TaxCodeId!.Value, FinancialDocumentId = id, SourceReference = $"AP:{invoice.Id:N}", TransactionDate = invoice.InvoiceDate, NetAmount = line.NetAmount, TaxAmount = line.TaxAmount, Direction = TaxDirection.Input });
        }, "SupplierInvoice.Recorded", id => new { InvoiceId = invoice.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(invoice));
    }

    public async Task<Result<FinancialOperationResponse>> RecordSupplierPaymentAsync(RecordSupplierPaymentRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        if (r.Amount <= 0 || !await dbcontext.SupplierAccounts.AnyAsync(x => x.Id == r.SupplierAccountId && x.LegalEntityId == r.LegalEntityId && x.IsActive, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var paymentCashAccountId = r.CashAccountId; var paymentPayableAccountId = r.PayableAccountId;
        if (paymentCashAccountId <= 0 && paymentPayableAccountId <= 0)
        {
            var route = await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.PaymentDate, "AP_PAYMENT", ct);
            if (route is null) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
            paymentPayableAccountId = route.DebitAccountId; paymentCashAccountId = route.CreditAccountId;
        }
        else if (paymentCashAccountId <= 0 || paymentPayableAccountId <= 0 || !await AccountsExistAsync(r.LegalEntityId, [paymentCashAccountId, paymentPayableAccountId], ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var payment = new SupplierPayment { LegalEntityId = r.LegalEntityId, SupplierAccountId = r.SupplierAccountId, PaymentNumber = r.PaymentNumber.Trim(), ExternalReference = r.ExternalReference.Trim(), PaymentDate = r.PaymentDate, Amount = Round(r.Amount), CashAccountId = paymentCashAccountId, PayableAccountId = paymentPayableAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId };
        var command = new PostSourceDocumentRequest(r.LegalEntityId, null, r.PaymentDate, "SupplierPayment", SourceReference("PAY", payment.PaymentNumber), payment.PostingProfileCode, $"Supplier payment {payment.PaymentNumber}", "SAR", r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Payables, [new PostingEventAmount("AP_PAYMENT", payment.Amount, payment.PaymentNumber)],
            CanonicalPayload(new { r.LegalEntityId, r.SupplierAccountId, PaymentNumber = payment.PaymentNumber, ExternalReference = payment.ExternalReference, r.PaymentDate, r.Amount, r.CashAccountId, r.PayableAccountId, PostingProfileCode = payment.PostingProfileCode }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            var original = await dbcontext.SupplierPayments.AsNoTracking().SingleOrDefaultAsync(x => x.FinancialDocumentId == replay.Value.Id, ct);
            return original is null ? Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState) : Result.Success(ToResponse(original));
        }
        if (await dbcontext.SupplierPayments.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && (x.PaymentNumber == payment.PaymentNumber || x.ExternalReference == payment.ExternalReference), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            payment.FinancialDocumentId = id; dbcontext.SupplierPayments.Add(payment);
        }, "SupplierPayment.Recorded", id => new { PaymentId = payment.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(payment));
    }

    public async Task<Result> AllocateSupplierPaymentAsync(Guid paymentId, AllocateSupplierPaymentRequest r, string actorId, CancellationToken ct = default)
    {
        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational()) transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (dbcontext.Database.IsSqlServer()) await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:APAllocation:" + paymentId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
            var payment = await dbcontext.SupplierPayments.Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == paymentId, ct); if (payment is null) return Result.Failure(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, payment.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return access;
            var invoice = await dbcontext.SupplierInvoices.SingleOrDefaultAsync(x => x.Id == r.SupplierInvoiceId && x.LegalEntityId == payment.LegalEntityId && x.SupplierAccountId == payment.SupplierAccountId, ct); if (invoice is null) return Result.Failure(FinancialOperationsErrors.NotFound); if (r.Amount <= 0 || invoice.Status is PayableInvoiceStatus.Draft or PayableInvoiceStatus.Cancelled) return Result.Failure(FinancialOperationsErrors.InvalidState);
            var amount = Round(r.Amount); var paymentRemaining = payment.Amount - payment.Allocations.Sum(x => x.Amount); var invoiceApplied = await dbcontext.SupplierPaymentAllocations.Where(x => x.SupplierInvoiceId == invoice.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m; if (amount > paymentRemaining || amount > invoice.GrossAmount - invoiceApplied) return Result.Failure(FinancialOperationsErrors.AllocationExceedsBalance);
            payment.Allocations.Add(new SupplierPaymentAllocation { SupplierInvoiceId = invoice.Id, Amount = amount, AllocatedBy = actorId }); invoiceApplied += amount; payment.Status = paymentRemaining == amount ? PaymentStatus.Applied : PaymentStatus.PartiallyApplied; invoice.Status = invoiceApplied == invoice.GrossAmount ? PayableInvoiceStatus.Paid : PayableInvoiceStatus.PartiallyPaid;
            await AuditAsync(payment.LegalEntityId, "SupplierPayment.Allocated", actorId, new { PaymentId = payment.Id, InvoiceId = invoice.Id, Amount = amount }, ct); await dbcontext.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return Result.Success();
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    public async Task<Result<MasterRecordResponse>> CreateInventoryItemAsync(CreateInventoryItemRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<MasterRecordResponse>(access.Error); var sku = Code(r.Sku); if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound); if (await dbcontext.InventoryItems.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Sku == sku, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.Duplicate);
        var item = new InventoryItem { LegalEntityId = r.LegalEntityId, Sku = sku, Name = r.Name.Trim(), UnitOfMeasure = r.UnitOfMeasure.Trim() }; dbcontext.InventoryItems.Add(item); await AuditAsync(r.LegalEntityId, "InventoryItem.Created", actorId, new { item.Id, item.Sku }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(item));
    }

    public async Task<Result<FinancialOperationResponse>> RecordInventoryMovementAsync(RecordInventoryMovementRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var eventCode = $"INVENTORY_{r.MovementType.ToString().ToUpperInvariant()}";
        var debitAccountId = r.DebitAccountId; var creditAccountId = r.CreditAccountId;
        if (debitAccountId <= 0 && creditAccountId <= 0)
        {
            var route = await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.MovementDate, eventCode, ct);
            if (route is null) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
            debitAccountId = route.DebitAccountId; creditAccountId = route.CreditAccountId;
        }
        if (r.Quantity <= 0 || r.UnitCost <= 0 || debitAccountId <= 0 || creditAccountId <= 0 || debitAccountId == creditAccountId || !await dbcontext.InventoryItems.AnyAsync(x => x.Id == r.InventoryItemId && x.LegalEntityId == r.LegalEntityId && x.IsActive, ct) || !await AccountsExistAsync(r.LegalEntityId, [debitAccountId, creditAccountId], ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var movement = new InventoryMovement { LegalEntityId = r.LegalEntityId, InventoryItemId = r.InventoryItemId, MovementType = r.MovementType, MovementDate = r.MovementDate, Reference = r.Reference.Trim(), FromBin = r.FromBin.Trim(), ToBin = r.ToBin.Trim(), Quantity = r.Quantity, UnitCost = r.UnitCost, DebitAccountId = debitAccountId, CreditAccountId = creditAccountId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId }; var amount = Round(movement.Quantity * movement.UnitCost);
        var command = new PostSourceDocumentRequest(r.LegalEntityId, null, r.MovementDate, "InventoryMovement", SourceReference("INV", movement.Reference), movement.PostingProfileCode, $"Inventory {movement.MovementType} {movement.Reference}", "SAR", r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Inventory, [new PostingEventAmount(eventCode, amount, movement.Reference)],
            CanonicalPayload(new { r.LegalEntityId, r.InventoryItemId, r.MovementType, r.MovementDate, Reference = movement.Reference, FromBin = movement.FromBin, ToBin = movement.ToBin, r.Quantity, r.UnitCost, r.DebitAccountId, r.CreditAccountId, PostingProfileCode = movement.PostingProfileCode }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            var original = await dbcontext.InventoryMovements.AsNoTracking().SingleOrDefaultAsync(x => x.FinancialDocumentId == replay.Value.Id, ct);
            return original is null ? Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState) : Result.Success(ToResponse(original));
        }
        if (await dbcontext.InventoryMovements.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Reference == movement.Reference, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            movement.FinancialDocumentId = id; dbcontext.InventoryMovements.Add(movement);
        }, "InventoryMovement.Recorded", id => new { MovementId = movement.Id, movement.Reference, FinancialDocumentId = id }, ct, async token =>
        {
            if (movement.MovementType is not (InventoryMovementType.Issue or InventoryMovementType.Transfer)) return Result.Success();
            if (string.IsNullOrWhiteSpace(movement.FromBin)) return Result.Failure(FinancialOperationsErrors.InvalidRequest);
            if (dbcontext.Database.IsSqlServer())
            {
                var resource = $"Accounting:InventoryStock:{movement.LegalEntityId}:{movement.InventoryItemId:N}:{movement.FromBin}";
                await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", token);
            }
            var history = await dbcontext.InventoryMovements.AsNoTracking().Where(x => x.LegalEntityId == movement.LegalEntityId && x.InventoryItemId == movement.InventoryItemId).ToListAsync(token);
            var available = history.Where(x => (x.MovementType is InventoryMovementType.Receipt or InventoryMovementType.Adjustment) && x.ToBin == movement.FromBin).Sum(x => x.Quantity)
                + history.Where(x => x.MovementType == InventoryMovementType.Transfer && x.ToBin == movement.FromBin).Sum(x => x.Quantity)
                - history.Where(x => x.MovementType is InventoryMovementType.Issue or InventoryMovementType.Transfer && x.FromBin == movement.FromBin).Sum(x => x.Quantity);
            return available >= movement.Quantity ? Result.Success() : Result.Failure(FinancialOperationsErrors.AllocationExceedsBalance);
        });
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(movement));
    }

    public async Task<Result<FinancialOperationResponse>> CreateExpenseClaimAsync(CreateExpenseClaimRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error);
        var expenseAccountId = r.ExpenseAccountId; var employeePayableAccountId = r.EmployeePayableAccountId;
        if (expenseAccountId <= 0 && employeePayableAccountId <= 0)
        {
            var route = await ResolvePostingRouteAsync(r.LegalEntityId, r.PostingProfileCode, r.ClaimDate, "EXPENSE_CLAIM_NET", ct);
            if (route is null) return Result.Failure<FinancialOperationResponse>(LedgerErrors.MissingPostingRoute);
            expenseAccountId = route.DebitAccountId; employeePayableAccountId = route.CreditAccountId;
        }
        if (r.NetAmount < 0 || expenseAccountId <= 0 || employeePayableAccountId <= 0 || !await dbcontext.Employees.AnyAsync(x => x.IqamaNo == r.EmployeeIqamaNo && !x.IsDeleted, ct) || !await AccountsExistAsync(r.LegalEntityId, [expenseAccountId, employeePayableAccountId], ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        if (r.SourceEvidenceId is not null && !await EvidenceAcceptedAsync(r.SourceEvidenceId.Value, r.LegalEntityId, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.EvidenceNotAccepted);
        TaxCode? tax = null; if (r.TaxCodeId is not null) { tax = await dbcontext.TaxCodes.SingleOrDefaultAsync(x => x.Id == r.TaxCodeId && x.LegalEntityId == r.LegalEntityId && x.Direction == TaxDirection.Input && x.IsActive && x.EffectiveFrom <= r.ClaimDate && (x.EffectiveTo == null || x.EffectiveTo >= r.ClaimDate), ct); if (tax is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest); }
        var claim = new ExpenseClaim { LegalEntityId = r.LegalEntityId, EmployeeIqamaNo = r.EmployeeIqamaNo, SourceEvidenceId = r.SourceEvidenceId, ClaimNumber = r.ClaimNumber.Trim(), ClaimDate = r.ClaimDate, Description = r.Description.Trim(), NetAmount = Round(r.NetAmount), TaxAmount = tax is null ? 0 : Round(r.NetAmount * tax.Rate), ExpenseAccountId = expenseAccountId, EmployeePayableAccountId = employeePayableAccountId, TaxCodeId = r.TaxCodeId, PostingProfileCode = Code(r.PostingProfileCode), CreatedBy = actorId };
        var events = new List<PostingEventAmount>();
        if (claim.NetAmount > 0) events.Add(new("EXPENSE_CLAIM_NET", claim.NetAmount, claim.Description));
        if (claim.TaxAmount > 0) events.Add(new("EXPENSE_INPUT_VAT", claim.TaxAmount, $"Input VAT {claim.ClaimNumber}"));
        var command = new PostSourceDocumentRequest(r.LegalEntityId, null, r.ClaimDate, "ExpenseClaim", SourceReference("EXP", claim.ClaimNumber), claim.PostingProfileCode, $"Expense claim {claim.ClaimNumber}", "SAR", r.IdempotencyKey, Correlation(r.IdempotencyKey), AccountingModule.Payables, events,
            CanonicalPayload(new { r.LegalEntityId, r.EmployeeIqamaNo, r.SourceEvidenceId, ClaimNumber = claim.ClaimNumber, r.ClaimDate, Description = claim.Description, r.NetAmount, r.ExpenseAccountId, r.EmployeePayableAccountId, r.TaxCodeId, PostingProfileCode = claim.PostingProfileCode }));
        var replay = await ReplayPostingAsync(command, actorId, ct);
        if (replay is not null)
        {
            if (replay.IsFailure) return Result.Failure<FinancialOperationResponse>(replay.Error);
            var original = await dbcontext.ExpenseClaims.AsNoTracking().SingleOrDefaultAsync(x => x.FinancialDocumentId == replay.Value.Id, ct);
            return original is null ? Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState) : Result.Success(ToResponse(original));
        }
        if (await dbcontext.ExpenseClaims.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.ClaimNumber == claim.ClaimNumber, ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var document = await PostAtomicallyAsync(command, actorId, id =>
        {
            claim.Status = ExpenseClaimStatus.Recorded; claim.FinancialDocumentId = id; dbcontext.ExpenseClaims.Add(claim);
            if (tax is not null) dbcontext.TaxTransactions.Add(new TaxTransaction { LegalEntityId = claim.LegalEntityId, TaxCodeId = tax.Id, FinancialDocumentId = id, SourceReference = $"EXP:{claim.Id:N}", TransactionDate = claim.ClaimDate, NetAmount = claim.NetAmount, TaxAmount = claim.TaxAmount, Direction = TaxDirection.Input });
        }, "ExpenseClaim.Recorded", id => new { ClaimId = claim.Id, FinancialDocumentId = id }, ct);
        if (document.IsFailure) return Result.Failure<FinancialOperationResponse>(document.Error);
        return Result.Success(ToResponse(claim));
    }

    public async Task<Result<MasterRecordResponse>> CreateBankAccountAsync(CreateBankAccountRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<MasterRecordResponse>(access.Error); var code = Code(r.Code); if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct) || !await AccountsExistAsync(r.LegalEntityId, [r.LedgerAccountId], ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.NotFound); if (await dbcontext.BankAccounts.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == code, ct)) return Result.Failure<MasterRecordResponse>(FinancialOperationsErrors.Duplicate);
        var bank = new BankAccount { LegalEntityId = r.LegalEntityId, Code = code, Name = r.Name.Trim(), CurrencyCode = Code(r.CurrencyCode), LedgerAccountId = r.LedgerAccountId }; dbcontext.BankAccounts.Add(bank); await AuditAsync(r.LegalEntityId, "BankAccount.Created", actorId, new { bank.Id, bank.Code }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(bank));
    }

    public async Task<Result<FinancialOperationResponse>> RecordBankStatementLineAsync(RecordBankStatementLineRequest r, string actorId, CancellationToken ct = default)
    {
        var bank = await dbcontext.BankAccounts.SingleOrDefaultAsync(x => x.Id == r.BankAccountId && x.IsActive, ct); if (bank is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, bank.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error); if (r.Amount == 0 || await dbcontext.BankStatementLines.AnyAsync(x => x.BankAccountId == r.BankAccountId && x.ExternalReference == r.ExternalReference.Trim(), ct)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.Duplicate);
        var line = new BankStatementLine { BankAccountId = r.BankAccountId, ExternalReference = r.ExternalReference.Trim(), TransactionDate = r.TransactionDate, Amount = Round(r.Amount), Description = r.Description.Trim() }; dbcontext.BankStatementLines.Add(line); await AuditAsync(bank.LegalEntityId, "BankStatement.Imported", actorId, new { StatementLineId = line.Id, BankAccountId = bank.Id, line.ExternalReference, line.Amount }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(new FinancialOperationResponse(line.Id, bank.LegalEntityId, line.ExternalReference, line.Status.ToString(), line.Amount, line.MatchedFinancialDocumentId));
    }

    public async Task<Result<FinancialOperationResponse>> ReconcileBankStatementLineAsync(Guid id, ReconcileBankStatementLineRequest r, string actorId, CancellationToken ct = default)
    {
        var line = await dbcontext.BankStatementLines.Include(x => x.BankAccount).SingleOrDefaultAsync(x => x.Id == id, ct); if (line is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, line.BankAccount.LegalEntityId, FinancialPermission.Approve, ct); if (access.IsFailure) return Result.Failure<FinancialOperationResponse>(access.Error); if (line.Status != BankStatementStatus.Unreconciled) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidState);
        var document = await dbcontext.FinancialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.FinancialDocumentId && x.LegalEntityId == line.BankAccount.LegalEntityId && x.Status == FinancialDocumentStatus.Posted, ct); if (document is null) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        var matchedAmount = await dbcontext.JournalLines.AsNoTracking().Where(x => x.JournalEntry.PostingBatch.FinancialDocumentId == document.Id && x.AccountId == line.BankAccount.LedgerAccountId && x.JournalEntry.IsFinalized).SumAsync(x => (decimal?)(x.Debit - x.Credit), ct) ?? 0m; if (Round(matchedAmount) != Round(line.Amount)) return Result.Failure<FinancialOperationResponse>(FinancialOperationsErrors.InvalidRequest);
        line.Status = BankStatementStatus.Reconciled; line.MatchedFinancialDocumentId = document.Id; line.ReconciledBy = actorId; line.ReconciledAt = DateTime.UtcNow; await AuditAsync(line.BankAccount.LegalEntityId, "BankStatement.Reconciled", actorId, new { StatementLineId = line.Id, FinancialDocumentId = document.Id }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(line));
    }

    public async Task<Result<TaxCodeResponse>> CreateTaxCodeAsync(CreateTaxCodeRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<TaxCodeResponse>(access.Error); var code = Code(r.Code); if (r.Rate is < 0 or > 1 || r.EffectiveTo < r.EffectiveFrom || !await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct) || !await AccountsExistAsync(r.LegalEntityId, [r.TaxAccountId], ct)) return Result.Failure<TaxCodeResponse>(FinancialOperationsErrors.InvalidRequest); if (await dbcontext.TaxCodes.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == code, ct)) return Result.Failure<TaxCodeResponse>(FinancialOperationsErrors.Duplicate);
        var tax = new TaxCode { LegalEntityId = r.LegalEntityId, Code = code, Name = r.Name.Trim(), Direction = r.Direction, Rate = r.Rate, TaxAccountId = r.TaxAccountId, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo }; dbcontext.TaxCodes.Add(tax); await AuditAsync(r.LegalEntityId, "TaxCode.Created", actorId, new { tax.Id, tax.Code, tax.Direction, tax.Rate }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(tax));
    }

    public async Task<Result<TaxReturnResponse>> PrepareTaxReturnAsync(PrepareTaxReturnRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<TaxReturnResponse>(access.Error); if (r.PeriodEnd < r.PeriodStart || await dbcontext.TaxReturns.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.PeriodStart == r.PeriodStart && x.PeriodEnd == r.PeriodEnd, ct)) return Result.Failure<TaxReturnResponse>(FinancialOperationsErrors.InvalidRequest);
        var transactions = await dbcontext.TaxTransactions.Where(x => x.LegalEntityId == r.LegalEntityId && x.TaxReturnId == null && x.TransactionDate >= r.PeriodStart && x.TransactionDate <= r.PeriodEnd && x.FinancialDocument != null && x.FinancialDocument.Status == FinancialDocumentStatus.Posted).ToListAsync(ct); var taxReturn = new TaxReturn { LegalEntityId = r.LegalEntityId, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd, OutputTaxAmount = transactions.Where(x => x.Direction == TaxDirection.Output).Sum(x => x.TaxAmount), InputTaxAmount = transactions.Where(x => x.Direction == TaxDirection.Input).Sum(x => x.TaxAmount), CreatedBy = actorId }; taxReturn.NetTaxPayableAmount = taxReturn.OutputTaxAmount - taxReturn.InputTaxAmount; foreach (var transaction in transactions) transaction.TaxReturnId = taxReturn.Id;
        dbcontext.TaxReturns.Add(taxReturn); await AuditAsync(r.LegalEntityId, "TaxReturn.Prepared", actorId, new { taxReturn.Id, taxReturn.PeriodStart, taxReturn.PeriodEnd, taxReturn.NetTaxPayableAmount }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(taxReturn));
    }

    public async Task<Result<TaxReturnResponse>> SubmitTaxReturnAsync(Guid id, SubmitTaxReturnRequest r, string actorId, CancellationToken ct = default)
    {
        var taxReturn = await dbcontext.TaxReturns.SingleOrDefaultAsync(x => x.Id == id, ct); if (taxReturn is null) return Result.Failure<TaxReturnResponse>(FinancialOperationsErrors.NotFound); var access = await RequireAsync(actorId, taxReturn.LegalEntityId, FinancialPermission.Approve, ct); if (access.IsFailure) return Result.Failure<TaxReturnResponse>(access.Error); if (taxReturn.Status != TaxReturnStatus.Draft || string.IsNullOrWhiteSpace(r.SubmissionReference)) return Result.Failure<TaxReturnResponse>(FinancialOperationsErrors.InvalidState);
        taxReturn.Status = TaxReturnStatus.Submitted; taxReturn.SubmissionReference = r.SubmissionReference.Trim(); taxReturn.SubmittedBy = actorId; taxReturn.SubmittedAt = DateTime.UtcNow; await AuditAsync(taxReturn.LegalEntityId, "TaxReturn.Submitted", actorId, new { taxReturn.Id, taxReturn.SubmissionReference }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(taxReturn));
    }

    public async Task<Result<FixedAssetResponse>> CreateFixedAssetAsync(CreateFixedAssetRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<FixedAssetResponse>(access.Error); if (r.AcquisitionCost < 0 || r.ResidualValue < 0 || r.ResidualValue > r.AcquisitionCost || r.UsefulLifeMonths <= 0 || !await AccountsExistAsync(r.LegalEntityId, [r.AssetAccountId, r.AccumulatedDepreciationAccountId, r.DepreciationExpenseAccountId], ct)) return Result.Failure<FixedAssetResponse>(FinancialOperationsErrors.InvalidRequest); if (await dbcontext.FixedAssets.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.AssetNumber == r.AssetNumber.Trim(), ct)) return Result.Failure<FixedAssetResponse>(FinancialOperationsErrors.Duplicate);
        var asset = new FixedAsset { LegalEntityId = r.LegalEntityId, AssetNumber = r.AssetNumber.Trim(), Description = r.Description.Trim(), AcquisitionDate = r.AcquisitionDate, AcquisitionCost = Round(r.AcquisitionCost), ResidualValue = Round(r.ResidualValue), UsefulLifeMonths = r.UsefulLifeMonths, AssetAccountId = r.AssetAccountId, AccumulatedDepreciationAccountId = r.AccumulatedDepreciationAccountId, DepreciationExpenseAccountId = r.DepreciationExpenseAccountId }; dbcontext.FixedAssets.Add(asset); await AuditAsync(r.LegalEntityId, "FixedAsset.Created", actorId, new { asset.Id, asset.AssetNumber }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(asset));
    }

    public async Task<Result<BudgetResponse>> CreateBudgetAsync(CreateBudgetRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<BudgetResponse>(access.Error); if (r.EndDate < r.StartDate || r.Lines.Count == 0 || await dbcontext.Budgets.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Name == r.Name.Trim(), ct) || !await AccountsExistAsync(r.LegalEntityId, r.Lines.Select(x => x.AccountId), ct)) return Result.Failure<BudgetResponse>(FinancialOperationsErrors.InvalidRequest);
        var dimensionIds = r.Lines.Where(x => x.FinancialDimensionValueId is not null).Select(x => x.FinancialDimensionValueId!.Value).ToArray(); if (dimensionIds.Length > 0 && await dbcontext.FinancialDimensionValues.CountAsync(x => x.IsActive && dimensionIds.Contains(x.Id) && x.FinancialDimension.LegalEntityId == r.LegalEntityId, ct) != dimensionIds.Length) return Result.Failure<BudgetResponse>(FinancialOperationsErrors.InvalidRequest);
        var budget = new Budget { LegalEntityId = r.LegalEntityId, Name = r.Name.Trim(), StartDate = r.StartDate, EndDate = r.EndDate, CreatedBy = actorId, Lines = r.Lines.Select(x => new BudgetLine { AccountId = x.AccountId, FinancialDimensionValueId = x.FinancialDimensionValueId, Amount = Round(x.Amount) }).ToList() }; dbcontext.Budgets.Add(budget); await AuditAsync(r.LegalEntityId, "Budget.Created", actorId, new { budget.Id, budget.Name, Total = budget.Lines.Sum(x => x.Amount) }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(budget));
    }

    private async Task<Result<FinancialDocumentResponse>> PostAtomicallyAsync(
        PostSourceDocumentRequest command,
        string actorId,
        Action<Guid> finalizeSubledger,
        string eventType,
        Func<Guid, object> auditPayload,
        CancellationToken ct,
        Func<CancellationToken, Task<Result>>? validateInsideTransaction = null)
    {
        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (validateInsideTransaction is not null)
            {
                var validation = await validateInsideTransaction(ct);
                if (validation.IsFailure)
                {
                    if (transaction is not null) await transaction.RollbackAsync(ct);
                    return Result.Failure<FinancialDocumentResponse>(validation.Error);
                }
            }
            var posting = await accountingPostingService.PostAsync(command, actorId, ct);
            if (posting.IsFailure)
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                return Result.Failure<FinancialDocumentResponse>(posting.Error);
            }
            finalizeSubledger(posting.Value.Id);
            await AuditAsync(command.LegalEntityId, eventType, actorId, auditPayload(posting.Value.Id), ct);
            await dbcontext.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return posting;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task<Result<FinancialDocumentResponse>?> ReplayPostingAsync(PostSourceDocumentRequest command, string actorId, CancellationToken ct)
    {
        var documentType = command.DocumentType.Trim();
        var idempotencyKey = command.IdempotencyKey.Trim();
        var exists = await dbcontext.FinancialDocuments.AsNoTracking().AnyAsync(
            x => x.LegalEntityId == command.LegalEntityId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey,
            ct);
        return exists ? await accountingPostingService.PostAsync(command, actorId, ct) : null;
    }

    private static string CanonicalPayload(object payload) => JsonSerializer.Serialize(payload);
    private static string SourceReference(string prefix, string businessReference)
    {
        var value = $"{prefix}:{businessReference.Trim()}";
        return value.Length <= 128 ? value : value[..128];
    }

    private async Task<Error?> ValidateRegisterAsync(AccountingRegisterFilter filter, string actorId, CancellationToken ct)
    {
        if (filter.LegalEntityId <= 0 || filter.FromDate > filter.ToDate ||
            (!string.IsNullOrWhiteSpace(filter.SortDirection) &&
             !string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(filter.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)))
            return FinancialOperationsErrors.InvalidRequest;
        var access = await RequireAsync(actorId, filter.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? access.Error : null;
    }

    private static async Task<PagedResponse<TResponse>> PageAsync<TEntity, TResponse, TId>(IQueryable<TEntity> query, PaginationRequest pagination, string sortProperty, string? sortDirection, Expression<Func<TEntity, TId>> idSelector, Func<TEntity, TResponse> map, CancellationToken ct)
        where TEntity : class
    {
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize;
        var total = await query.CountAsync(ct);
        var ordered = IsDescending(sortDirection)
            ? query.OrderByDescending(x => EF.Property<object>(x, sortProperty)).ThenByDescending(idSelector)
            : query.OrderBy(x => EF.Property<object>(x, sortProperty)).ThenBy(idSelector);
        var records = await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<TResponse>(records.Select(map).ToList(), pageNumber, pageSize, total);
    }

    private static IQueryable<TEntity> ApplyCreatedDateFilter<TEntity>(IQueryable<TEntity> query, DateOnly? fromDate, DateOnly? toDate, Expression<Func<TEntity, DateTime>> selector)
    {
        if (fromDate.HasValue)
        {
            var lower = Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(fromDate.Value.ToDateTime(TimeOnly.MinValue)));
            query = query.Where(Expression.Lambda<Func<TEntity, bool>>(lower, selector.Parameters));
        }
        if (toDate.HasValue)
        {
            var upper = Expression.LessThanOrEqual(selector.Body, Expression.Constant(toDate.Value.ToDateTime(TimeOnly.MaxValue)));
            query = query.Where(Expression.Lambda<Func<TEntity, bool>>(upper, selector.Parameters));
        }
        return query;
    }

    private static string SortProperty(string? requested, string fallback, params (string Alias, string Property)[] supported)
    {
        if (string.IsNullOrWhiteSpace(requested)) return fallback;
        var normalized = new string(requested.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return supported.FirstOrDefault(x => x.Alias == normalized).Property ?? fallback;
    }

    private static string Search(string value) => value.Trim().ToLowerInvariant();
    private static bool IsDescending(string? direction) => !string.Equals(direction?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
    private Task<Result> RequireAsync(string actorId, int legalEntityId, FinancialPermission permission, CancellationToken ct) => financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, permission, ct);
    private Task<bool> EvidenceAcceptedAsync(Guid id, int legalEntityId, CancellationToken ct) => dbcontext.SourceEvidences.AnyAsync(x => x.Id == id && x.LegalEntityId == legalEntityId && x.Status == SourceEvidenceStatus.Accepted, ct);
    private async Task<bool> AccountsExistAsync(int legalEntityId, IEnumerable<int> accountIds, CancellationToken ct) { var ids = accountIds.Distinct().ToArray(); return ids.Length > 0 && await dbcontext.AccountingAccounts.CountAsync(x => x.LegalEntityId == legalEntityId && x.IsActive && ids.Contains(x.Id), ct) == ids.Length; }
    private Task<PostingProfileLine?> ResolvePostingRouteAsync(int legalEntityId, string postingProfileCode, DateOnly effectiveDate, string eventCode, CancellationToken ct) =>
        dbcontext.PostingProfileLines.AsNoTracking()
            .Where(x => x.PostingProfile.LegalEntityId == legalEntityId && x.PostingProfile.Code == Code(postingProfileCode) && x.PostingProfile.IsActive && x.PostingProfile.EffectiveFrom <= effectiveDate && (x.PostingProfile.EffectiveTo == null || x.PostingProfile.EffectiveTo >= effectiveDate) && x.EventCode == eventCode)
            .SingleOrDefaultAsync(ct);
    private async Task<Dictionary<int, TaxCode>?> LoadTaxesAsync(int legalEntityId, IEnumerable<int?> requestedIds, TaxDirection direction, DateOnly date, CancellationToken ct) { var ids = requestedIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray(); var taxes = await dbcontext.TaxCodes.Where(x => ids.Contains(x.Id) && x.LegalEntityId == legalEntityId && x.IsActive && x.Direction == direction && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).ToDictionaryAsync(x => x.Id, ct); return taxes.Count == ids.Length ? taxes : null; }
    private async Task AuditAsync(int legalEntityId, string type, string actorId, object payload, CancellationToken ct)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + legalEntityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == legalEntityId, ct);
        if (head is null)
        {
            head = new AccountingAuditChainHead { LegalEntityId = legalEntityId };
            dbcontext.AccountingAuditChainHeads.Add(head);
        }
        var json = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{legalEntityId}||{type}|{actorId}|{json}")));
        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent { LegalEntityId = legalEntityId, EventType = type, ActorId = actorId, PayloadJson = json, PreviousHash = head.LastHash, Hash = hash });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = legalEntityId, Type = type, PayloadJson = json, CorrelationId = hash[..32] });
        head.LastHash = hash;
    }
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Correlation(string idempotencyKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())))[..32];
    private static string Code(string value) => value.Trim().ToUpperInvariant();
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static MasterRecordResponse ToResponse(CustomerAccount x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.IsActive, x.TaxRegistrationNumber, CreatedAt: x.CreatedAt);
    private static MasterRecordResponse ToResponse(SupplierAccount x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.IsActive, x.TaxRegistrationNumber, CreatedAt: x.CreatedAt);
    private static MasterRecordResponse ToResponse(InventoryItem x) => new(x.Id, x.LegalEntityId, x.Sku, x.Name, x.IsActive, UnitOfMeasure: x.UnitOfMeasure, CreatedAt: x.CreatedAt);
    private static MasterRecordResponse ToResponse(BankAccount x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.IsActive, CurrencyCode: x.CurrencyCode, LedgerAccountId: x.LedgerAccountId, CreatedAt: x.CreatedAt);
    private static SourceEvidenceResponse ToResponse(SourceEvidence x) => new(x.Id, x.LegalEntityId, x.PlatformAccountId, x.StoredFileId, x.EvidenceType, x.ExternalReference, x.ContentHash, x.Status, x.ReceivedAt, x.ReviewedBy, x.MetadataJson, x.ReceivedBy, x.ReviewedAt, x.ReviewComment);
    private static FinancialOperationResponse ToResponse(PlatformSettlement x) => new(x.Id, x.LegalEntityId, x.SettlementReference, x.Status.ToString(), x.NetSettlementAmount, x.FinancialDocumentId, SourceEvidenceId: x.SourceEvidenceId, TransactionDate: x.SettlementDate, NetAmount: x.NetSettlementAmount, PostingProfileCode: x.PostingProfileCode, GrossAmount: x.GrossRevenue, CommissionAmount: x.CommissionAmount, PlatformClearingAccountId: x.PlatformClearingAccountId, CommissionExpenseAccountId: x.CommissionExpenseAccountId, RevenueAccountId: x.RevenueAccountId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static FinancialOperationResponse ToResponse(CustomerInvoice x, decimal? openAmount = null) => new(
        x.Id, x.LegalEntityId, x.InvoiceNumber, x.Status.ToString(), x.GrossAmount, x.FinancialDocumentId,
        CounterpartyId: x.CustomerAccountId, SourceEvidenceId: x.SourceEvidenceId, TransactionDate: x.InvoiceDate, DueDate: x.DueDate,
        CurrencyCode: x.CurrencyCode, ExchangeRate: x.ExchangeRate, NetAmount: x.NetAmount, TaxAmount: x.TaxAmount,
        OpenAmount: openAmount, PostingProfileCode: x.PostingProfileCode, GrossAmount: x.GrossAmount,
        Lines: x.Lines.OrderBy(l => l.LineNumber).Select(l => new FinancialOperationLineResponse(l.LineNumber, l.Description, l.Quantity, l.UnitPrice, l.NetAmount, l.TaxAmount, l.RevenueAccountId, l.TaxCodeId)).ToArray(),
        ReceivableAccountId: x.ReceivableAccountId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static FinancialOperationResponse ToResponse(CustomerReceipt x) => new(
        x.Id, x.LegalEntityId, x.ReceiptNumber, x.Status.ToString(), x.Amount, x.FinancialDocumentId,
        CounterpartyId: x.CustomerAccountId, TransactionDate: x.ReceiptDate, CurrencyCode: x.CurrencyCode, ExchangeRate: x.ExchangeRate,
        UnappliedAmount: Math.Max(0m, x.Amount - x.Allocations.Sum(a => a.Amount)), ExternalReference: x.ExternalReference,
        PostingProfileCode: x.PostingProfileCode,
        Allocations: x.Allocations.OrderBy(a => a.AllocatedAt).ThenBy(a => a.Id).Select(a => new FinancialOperationAllocationResponse(a.CustomerInvoiceId, a.Amount, a.AllocatedAt, a.AllocatedBy)).ToArray(),
        ReceivableAccountId: x.ReceivableAccountId, CashAccountId: x.CashAccountId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static PayrollRunResponse ToResponse(PayrollRun x) => new(x.Id, x.LegalEntityId, x.RunNumber, x.PeriodStart, x.PeriodEnd, x.Status, x.GrossAmount, x.DeductionAmount, x.NetAmount, x.AccrualFinancialDocumentId, x.PaymentFinancialDocumentId);
    private static FinancialOperationResponse ToResponse(SupplierInvoice x, decimal? openAmount = null) => new(
        x.Id, x.LegalEntityId, x.InvoiceNumber, x.Status.ToString(), x.GrossAmount, x.FinancialDocumentId,
        CounterpartyId: x.SupplierAccountId, SourceEvidenceId: x.SourceEvidenceId, TransactionDate: x.InvoiceDate, DueDate: x.DueDate,
        CurrencyCode: x.CurrencyCode, ExchangeRate: x.ExchangeRate, NetAmount: x.NetAmount, TaxAmount: x.TaxAmount,
        OpenAmount: openAmount, PostingProfileCode: x.PostingProfileCode, GrossAmount: x.GrossAmount,
        Lines: x.Lines.OrderBy(l => l.LineNumber).Select(l => new FinancialOperationLineResponse(l.LineNumber, l.Description, l.Quantity, l.UnitPrice, l.NetAmount, l.TaxAmount, l.ExpenseOrInventoryAccountId, l.TaxCodeId)).ToArray(),
        PayableAccountId: x.PayableAccountId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static FinancialOperationResponse ToResponse(SupplierPayment x) => new(
        x.Id, x.LegalEntityId, x.PaymentNumber, x.Status.ToString(), x.Amount, x.FinancialDocumentId,
        CounterpartyId: x.SupplierAccountId, TransactionDate: x.PaymentDate,
        UnappliedAmount: Math.Max(0m, x.Amount - x.Allocations.Sum(a => a.Amount)), ExternalReference: x.ExternalReference,
        PostingProfileCode: x.PostingProfileCode,
        Allocations: x.Allocations.OrderBy(a => a.AllocatedAt).ThenBy(a => a.Id).Select(a => new FinancialOperationAllocationResponse(a.SupplierInvoiceId, a.Amount, a.AllocatedAt, a.AllocatedBy)).ToArray(),
        PayableAccountId: x.PayableAccountId, CashAccountId: x.CashAccountId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static FinancialOperationResponse ToResponse(ExpenseClaim x) => new(x.Id, x.LegalEntityId, x.ClaimNumber, x.Status.ToString(), x.NetAmount + x.TaxAmount, x.FinancialDocumentId, SourceEvidenceId: x.SourceEvidenceId, TransactionDate: x.ClaimDate, NetAmount: x.NetAmount, TaxAmount: x.TaxAmount, Description: x.Description, PostingProfileCode: x.PostingProfileCode, EmployeeIqamaNo: x.EmployeeIqamaNo, ExpenseAccountId: x.ExpenseAccountId, EmployeePayableAccountId: x.EmployeePayableAccountId, TaxCodeId: x.TaxCodeId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static FinancialOperationResponse ToResponse(InventoryMovement x) => new(x.Id, x.LegalEntityId, x.Reference, x.MovementType.ToString(), Round(x.Quantity * x.UnitCost), x.FinancialDocumentId, TransactionDate: x.MovementDate, PostingProfileCode: x.PostingProfileCode, InventoryItemId: x.InventoryItemId, MovementType: x.MovementType, FromBin: x.FromBin, ToBin: x.ToBin, Quantity: x.Quantity, UnitCost: x.UnitCost, DebitAccountId: x.DebitAccountId, CreditAccountId: x.CreditAccountId, CreatedBy: x.CreatedBy, CreatedAt: x.CreatedAt);
    private static FinancialOperationResponse ToResponse(BankStatementLine x) => new(x.Id, x.BankAccount.LegalEntityId, x.ExternalReference, x.Status.ToString(), x.Amount, x.MatchedFinancialDocumentId, TransactionDate: x.TransactionDate, ExternalReference: x.ExternalReference, Description: x.Description, BankAccountId: x.BankAccountId, ReconciledBy: x.ReconciledBy, ReconciledAt: x.ReconciledAt, ImportedAt: x.ImportedAt);
    private static TaxCodeResponse ToResponse(TaxCode x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.Direction, x.Rate, x.TaxAccountId, x.IsActive, x.EffectiveFrom, x.EffectiveTo);
    private static TaxReturnResponse ToResponse(TaxReturn x) => new(x.Id, x.LegalEntityId, x.PeriodStart, x.PeriodEnd, x.OutputTaxAmount, x.InputTaxAmount, x.NetTaxPayableAmount, x.Status, x.SubmissionReference, x.CreatedBy, x.CreatedAt, x.SubmittedBy, x.SubmittedAt, x.TaxTransactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).Select(t => new TaxTransactionResponse(t.Id, t.TaxCodeId, t.FinancialDocumentId, t.SourceReference, t.TransactionDate, t.NetAmount, t.TaxAmount, t.Direction)).ToArray());
    private static FixedAssetResponse ToResponse(FixedAsset x) => new(x.Id, x.LegalEntityId, x.AssetNumber, x.Status, x.AcquisitionCost, x.ResidualValue, x.UsefulLifeMonths, x.Description, x.AcquisitionDate, x.AssetAccountId, x.AccumulatedDepreciationAccountId, x.DepreciationExpenseAccountId, x.CreatedAt);
    private static BudgetResponse ToResponse(Budget x) => new(x.Id, x.LegalEntityId, x.Name, x.StartDate, x.EndDate, x.IsApproved, x.Lines.Sum(x => x.Amount), x.CreatedBy, x.CreatedAt, x.Lines.OrderBy(l => l.Id).Select(l => new BudgetLineResponse(l.AccountId, l.FinancialDimensionValueId, l.Amount)).ToArray());
}
