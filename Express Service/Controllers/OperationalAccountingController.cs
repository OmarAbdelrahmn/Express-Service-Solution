using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.FinancialOperations;
using Application.Extensions;
using Application.Service.FinancialOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class OperationalAccountingController(IFinancialOperationsService service) : ControllerBase
{
    [HttpGet("receivables/customers")]
    public Task<IActionResult> GetCustomers([FromQuery] PaginationRequest pagination, [FromQuery] MasterRecordListFilter filter, CancellationToken ct) => WithActor(a => service.GetCustomerAccountsAsync(pagination, filter, a, ct));
    [HttpGet("receivables/customers/{id:guid}")]
    public Task<IActionResult> GetCustomer(Guid id, CancellationToken ct) => WithActor(a => service.GetCustomerAccountAsync(id, a, ct));
    [HttpPost("receivables/customers")]
    public Task<IActionResult> CreateCustomer(CreateCustomerAccountRequest request, CancellationToken ct) => WithActor(a => service.CreateCustomerAccountAsync(request, a, ct));
    [HttpGet("receivables/invoices")]
    public Task<IActionResult> GetCustomerInvoices([FromQuery] PaginationRequest pagination, [FromQuery] CustomerInvoiceListFilter filter, CancellationToken ct) => WithActor(a => service.GetCustomerInvoicesAsync(pagination, filter, a, ct));
    [HttpGet("receivables/invoices/{id:guid}")]
    public Task<IActionResult> GetCustomerInvoice(Guid id, CancellationToken ct) => WithActor(a => service.GetCustomerInvoiceAsync(id, a, ct));
    [HttpPost("receivables/invoices")]
    public Task<IActionResult> CreateCustomerInvoice(AccountingCustomerInvoiceRequest request, CancellationToken ct) => WithActor(a => service.CreateCustomerInvoiceAsync(
        new CreateCustomerInvoiceRequest(request.LegalEntityId, request.CustomerAccountId, request.SourceEvidenceId, request.InvoiceNumber, request.InvoiceDate, request.DueDate, request.CurrencyCode, request.ExchangeRate, 0, request.PostingProfileCode,
            request.Lines.Select(x => new CreateCustomerInvoiceLineRequest(x.Description, x.Quantity, x.UnitPrice, 0, x.TaxCodeId)).ToArray()), a, ct));
    [HttpPost("receivables/invoices/{id:guid}/issue")]
    public Task<IActionResult> IssueCustomerInvoice(Guid id, IssueCustomerInvoiceRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.IssueCustomerInvoiceAsync(id, request with { IdempotencyKey = key }, a, ct)));
    [HttpGet("receivables/receipts")]
    public Task<IActionResult> GetCustomerReceipts([FromQuery] PaginationRequest pagination, [FromQuery] CustomerReceiptListFilter filter, CancellationToken ct) => WithActor(a => service.GetCustomerReceiptsAsync(pagination, filter, a, ct));
    [HttpGet("receivables/receipts/{id:guid}")]
    public Task<IActionResult> GetCustomerReceipt(Guid id, CancellationToken ct) => WithActor(a => service.GetCustomerReceiptAsync(id, a, ct));
    [HttpPost("receivables/receipts")]
    public Task<IActionResult> RecordCustomerReceipt(AccountingCustomerReceiptRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.RecordCustomerReceiptAsync(
        new RecordCustomerReceiptRequest(request.LegalEntityId, request.CustomerAccountId, request.ReceiptNumber, request.ExternalReference, request.ReceiptDate, request.CurrencyCode, request.ExchangeRate, request.Amount, 0, 0, request.PostingProfileCode, key), a, ct)));
    [HttpPost("receivables/receipts/{id:guid}/allocations")]
    public Task<IActionResult> AllocateCustomerReceipt(Guid id, AllocateCustomerReceiptRequest request, CancellationToken ct) => WithActor(a => service.AllocateCustomerReceiptAsync(id, request, a, ct));
    [HttpGet("receivables/platform-settlements")]
    public Task<IActionResult> GetPlatformSettlements([FromQuery] PaginationRequest pagination, [FromQuery] PlatformSettlementListFilter filter, CancellationToken ct) => WithActor(a => service.GetPlatformSettlementsAsync(pagination, filter, a, ct));
    [HttpGet("receivables/platform-settlements/{id:guid}")]
    public Task<IActionResult> GetPlatformSettlement(Guid id, CancellationToken ct) => WithActor(a => service.GetPlatformSettlementAsync(id, a, ct));
    [HttpPost("receivables/platform-settlements")]
    public Task<IActionResult> RecordPlatformSettlement(AccountingPlatformSettlementRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.RecordPlatformSettlementAsync(
        new RecordPlatformSettlementRequest(request.LegalEntityId, request.SourceEvidenceId, request.SettlementReference, request.SettlementDate, request.GrossRevenue, request.CommissionAmount, request.NetSettlementAmount, 0, 0, 0, request.PostingProfileCode, key), a, ct)));

    [HttpGet("payables/suppliers")]
    public Task<IActionResult> GetSuppliers([FromQuery] PaginationRequest pagination, [FromQuery] MasterRecordListFilter filter, CancellationToken ct) => WithActor(a => service.GetSupplierAccountsAsync(pagination, filter, a, ct));
    [HttpGet("payables/suppliers/{id:guid}")]
    public Task<IActionResult> GetSupplier(Guid id, CancellationToken ct) => WithActor(a => service.GetSupplierAccountAsync(id, a, ct));
    [HttpPost("payables/suppliers")]
    public Task<IActionResult> CreateSupplier(CreateSupplierAccountRequest request, CancellationToken ct) => WithActor(a => service.CreateSupplierAccountAsync(request, a, ct));
    [HttpGet("payables/invoices")]
    public Task<IActionResult> GetSupplierInvoices([FromQuery] PaginationRequest pagination, [FromQuery] SupplierInvoiceListFilter filter, CancellationToken ct) => WithActor(a => service.GetSupplierInvoicesAsync(pagination, filter, a, ct));
    [HttpGet("payables/invoices/{id:guid}")]
    public Task<IActionResult> GetSupplierInvoice(Guid id, CancellationToken ct) => WithActor(a => service.GetSupplierInvoiceAsync(id, a, ct));
    [HttpPost("payables/invoices")]
    public Task<IActionResult> CreateSupplierInvoice(AccountingSupplierInvoiceRequest request, CancellationToken ct) => WithActor(a => service.CreateSupplierInvoiceAsync(
        new CreateSupplierInvoiceRequest(request.LegalEntityId, request.SupplierAccountId, request.SourceEvidenceId, request.InvoiceNumber, request.InvoiceDate, request.DueDate, request.CurrencyCode, request.ExchangeRate, 0, request.PostingProfileCode,
            request.Lines.Select(x => new CreateSupplierInvoiceLineRequest(x.Description, x.Quantity, x.UnitPrice, 0, x.TaxCodeId)).ToArray()), a, ct));
    [HttpPost("payables/invoices/{id:guid}/record")]
    public Task<IActionResult> RecordSupplierInvoice(Guid id, RecordSupplierInvoiceRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.RecordSupplierInvoiceAsync(id, request with { IdempotencyKey = key }, a, ct)));
    [HttpGet("payables/payments")]
    public Task<IActionResult> GetSupplierPayments([FromQuery] PaginationRequest pagination, [FromQuery] SupplierPaymentListFilter filter, CancellationToken ct) => WithActor(a => service.GetSupplierPaymentsAsync(pagination, filter, a, ct));
    [HttpGet("payables/payments/{id:guid}")]
    public Task<IActionResult> GetSupplierPayment(Guid id, CancellationToken ct) => WithActor(a => service.GetSupplierPaymentAsync(id, a, ct));
    [HttpPost("payables/payments")]
    public Task<IActionResult> RecordSupplierPayment(AccountingSupplierPaymentRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.RecordSupplierPaymentAsync(
        new RecordSupplierPaymentRequest(request.LegalEntityId, request.SupplierAccountId, request.PaymentNumber, request.ExternalReference, request.PaymentDate, request.Amount, 0, 0, request.PostingProfileCode, key), a, ct)));
    [HttpPost("payables/payments/{id:guid}/allocations")]
    public Task<IActionResult> AllocateSupplierPayment(Guid id, AllocateSupplierPaymentRequest request, CancellationToken ct) => WithActor(a => service.AllocateSupplierPaymentAsync(id, request, a, ct));

    [HttpGet("expenses/evidence")]
    public Task<IActionResult> GetEvidence([FromQuery] PaginationRequest pagination, [FromQuery] SourceEvidenceListFilter filter, CancellationToken ct) => WithActor(a => service.GetSourceEvidenceAsync(pagination, filter, a, ct));
    [HttpGet("expenses/evidence/{id:guid}")]
    public Task<IActionResult> GetEvidence(Guid id, CancellationToken ct) => WithActor(a => service.GetSourceEvidenceAsync(id, a, ct));
    [HttpPost("expenses/evidence")]
    public Task<IActionResult> CreateEvidence(CreatePrivateSourceEvidenceRequest request, CancellationToken ct) => WithActor(a => service.CreatePrivateSourceEvidenceAsync(request, a, ct));
    [HttpPost("expenses/evidence/{id:guid}/review")]
    public Task<IActionResult> ReviewEvidence(Guid id, ReviewSourceEvidenceRequest request, CancellationToken ct) => WithActor(a => service.ReviewSourceEvidenceAsync(id, request, a, ct));
    [HttpGet("expenses/claims")]
    public Task<IActionResult> GetExpenseClaims([FromQuery] PaginationRequest pagination, [FromQuery] ExpenseClaimListFilter filter, CancellationToken ct) => WithActor(a => service.GetExpenseClaimsAsync(pagination, filter, a, ct));
    [HttpGet("expenses/claims/{id:guid}")]
    public Task<IActionResult> GetExpenseClaim(Guid id, CancellationToken ct) => WithActor(a => service.GetExpenseClaimAsync(id, a, ct));
    [HttpPost("expenses/claims")]
    public Task<IActionResult> CreateExpenseClaim(AccountingExpenseClaimRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.CreateExpenseClaimAsync(
        new CreateExpenseClaimRequest(request.LegalEntityId, request.EmployeeIqamaNo, request.SourceEvidenceId, request.ClaimNumber, request.ClaimDate, request.Description, request.NetAmount, 0, 0, request.TaxCodeId, request.PostingProfileCode, key), a, ct)));

    [HttpGet("inventory/items")]
    public Task<IActionResult> GetInventoryItems([FromQuery] PaginationRequest pagination, [FromQuery] MasterRecordListFilter filter, CancellationToken ct) => WithActor(a => service.GetInventoryItemsAsync(pagination, filter, a, ct));
    [HttpGet("inventory/items/{id:guid}")]
    public Task<IActionResult> GetInventoryItem(Guid id, CancellationToken ct) => WithActor(a => service.GetInventoryItemAsync(id, a, ct));
    [HttpPost("inventory/items")]
    public Task<IActionResult> CreateInventoryItem(CreateInventoryItemRequest request, CancellationToken ct) => WithActor(a => service.CreateInventoryItemAsync(request, a, ct));
    [HttpGet("inventory/movements")]
    public Task<IActionResult> GetInventoryMovements([FromQuery] PaginationRequest pagination, [FromQuery] InventoryMovementListFilter filter, CancellationToken ct) => WithActor(a => service.GetInventoryMovementsAsync(pagination, filter, a, ct));
    [HttpGet("inventory/movements/{id:guid}")]
    public Task<IActionResult> GetInventoryMovement(Guid id, CancellationToken ct) => WithActor(a => service.GetInventoryMovementAsync(id, a, ct));
    [HttpPost("inventory/movements")]
    public Task<IActionResult> RecordInventoryMovement(AccountingInventoryMovementRequest request, CancellationToken ct) => WithKey(key => WithActor(a => service.RecordInventoryMovementAsync(
        new RecordInventoryMovementRequest(request.LegalEntityId, request.InventoryItemId, request.MovementType, request.MovementDate, request.Reference, request.FromBin, request.ToBin, request.Quantity, request.UnitCost, 0, 0, request.PostingProfileCode, key), a, ct)));
    [HttpGet("inventory/stock-balances")]
    public Task<IActionResult> GetInventoryStockBalances([FromQuery] PaginationRequest pagination, [FromQuery] InventoryStockBalanceListFilter filter, CancellationToken ct) => WithActor(a => service.GetInventoryStockBalancesAsync(pagination, filter, a, ct));

    [HttpGet("treasury/bank-accounts")]
    public Task<IActionResult> GetBankAccounts([FromQuery] PaginationRequest pagination, [FromQuery] MasterRecordListFilter filter, CancellationToken ct) => WithActor(a => service.GetBankAccountsAsync(pagination, filter, a, ct));
    [HttpGet("treasury/bank-accounts/{id:guid}")]
    public Task<IActionResult> GetBankAccount(Guid id, CancellationToken ct) => WithActor(a => service.GetBankAccountAsync(id, a, ct));
    [HttpPost("treasury/bank-accounts")]
    public Task<IActionResult> CreateBankAccount(CreateBankAccountRequest request, CancellationToken ct) => WithActor(a => service.CreateBankAccountAsync(request, a, ct));
    [HttpGet("treasury/statement-lines")]
    public Task<IActionResult> GetStatementLines([FromQuery] PaginationRequest pagination, [FromQuery] BankStatementLineListFilter filter, CancellationToken ct) => WithActor(a => service.GetBankStatementLinesAsync(pagination, filter, a, ct));
    [HttpGet("treasury/statement-lines/{id:guid}")]
    public Task<IActionResult> GetStatementLine(Guid id, CancellationToken ct) => WithActor(a => service.GetBankStatementLineAsync(id, a, ct));
    [HttpPost("treasury/statement-lines")]
    public Task<IActionResult> RecordStatementLine(RecordBankStatementLineRequest request, CancellationToken ct) => WithActor(a => service.RecordBankStatementLineAsync(request, a, ct));
    [HttpPost("treasury/statement-lines/{id:guid}/reconcile")]
    public Task<IActionResult> ReconcileStatementLine(Guid id, ReconcileBankStatementLineRequest request, CancellationToken ct) => WithActor(a => service.ReconcileBankStatementLineAsync(id, request, a, ct));

    [HttpGet("tax/codes")]
    public Task<IActionResult> GetTaxCodes([FromQuery] PaginationRequest pagination, [FromQuery] TaxCodeListFilter filter, CancellationToken ct) => WithActor(a => service.GetTaxCodesAsync(pagination, filter, a, ct));
    [HttpGet("tax/codes/{id:int}")]
    public Task<IActionResult> GetTaxCode(int id, CancellationToken ct) => WithActor(a => service.GetTaxCodeAsync(id, a, ct));
    [HttpPost("tax/codes")]
    public Task<IActionResult> CreateTaxCode(CreateTaxCodeRequest request, CancellationToken ct) => WithActor(a => service.CreateTaxCodeAsync(request, a, ct));
    [HttpGet("tax/returns")]
    public Task<IActionResult> GetTaxReturns([FromQuery] PaginationRequest pagination, [FromQuery] TaxReturnListFilter filter, CancellationToken ct) => WithActor(a => service.GetTaxReturnsAsync(pagination, filter, a, ct));
    [HttpGet("tax/returns/{id:guid}")]
    public Task<IActionResult> GetTaxReturn(Guid id, CancellationToken ct) => WithActor(a => service.GetTaxReturnAsync(id, a, ct));
    [HttpPost("tax/returns")]
    public Task<IActionResult> PrepareTaxReturn(PrepareTaxReturnRequest request, CancellationToken ct) => WithActor(a => service.PrepareTaxReturnAsync(request, a, ct));
    [HttpPost("tax/returns/{id:guid}/submit")]
    public Task<IActionResult> SubmitTaxReturn(Guid id, SubmitTaxReturnRequest request, CancellationToken ct) => WithActor(a => service.SubmitTaxReturnAsync(id, request, a, ct));

    [HttpGet("assets")]
    public Task<IActionResult> GetFixedAssets([FromQuery] PaginationRequest pagination, [FromQuery] FixedAssetListFilter filter, CancellationToken ct) => WithActor(a => service.GetFixedAssetsAsync(pagination, filter, a, ct));
    [HttpGet("assets/{id:guid}")]
    public Task<IActionResult> GetFixedAsset(Guid id, CancellationToken ct) => WithActor(a => service.GetFixedAssetAsync(id, a, ct));
    [HttpPost("assets")]
    public Task<IActionResult> CreateFixedAsset(CreateFixedAssetRequest request, CancellationToken ct) => WithActor(a => service.CreateFixedAssetAsync(request, a, ct));
    [HttpGet("budgets")]
    public Task<IActionResult> GetBudgets([FromQuery] PaginationRequest pagination, [FromQuery] BudgetListFilter filter, CancellationToken ct) => WithActor(a => service.GetBudgetsAsync(pagination, filter, a, ct));
    [HttpGet("budgets/{id:guid}")]
    public Task<IActionResult> GetBudget(Guid id, CancellationToken ct) => WithActor(a => service.GetBudgetAsync(id, a, ct));
    [HttpPost("budgets")]
    public Task<IActionResult> CreateBudget(CreateBudgetRequest request, CancellationToken ct) => WithActor(a => service.CreateBudgetAsync(request, a, ct));

    private Task<IActionResult> WithKey(Func<string, Task<IActionResult>> action)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(key)
            ? Task.FromResult<IActionResult>(Result.Failure(AccountingPlatformErrors.IdempotencyKeyRequired).ToProblem())
            : action(key.Trim());
    }

    private async Task<IActionResult> WithActor<T>(Func<string, Task<Application.Abstraction.Result<T>>> action)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await action(actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    private async Task<IActionResult> WithActor(Func<string, Task<Application.Abstraction.Result>> action)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await action(actor);
        return result.IsSuccess ? Ok() : result.ToProblem();
    }
}
