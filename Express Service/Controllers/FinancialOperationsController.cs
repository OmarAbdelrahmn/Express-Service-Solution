using Application.Contracts.FinancialOperations;
using Application.Extensions;
using Application.Service.FinancialOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/financial-operations")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class FinancialOperationsController(IFinancialOperationsService service) : ControllerBase
{
    [HttpPost("source-evidence")]
    public async Task<IActionResult> CreateSourceEvidence([FromBody] CreateSourceEvidenceRequest request, CancellationToken ct) => await WithActor(actor => service.CreateSourceEvidenceAsync(request, actor, ct));
    [HttpPost("source-evidence/{id:guid}/review")]
    public async Task<IActionResult> ReviewSourceEvidence(Guid id, [FromBody] ReviewSourceEvidenceRequest request, CancellationToken ct) => await WithActor(actor => service.ReviewSourceEvidenceAsync(id, request, actor, ct));
    [HttpPost("platform-settlements")]
    public async Task<IActionResult> RecordPlatformSettlement([FromBody] RecordPlatformSettlementRequest request, CancellationToken ct) => await WithActor(actor => service.RecordPlatformSettlementAsync(request, actor, ct));

    [HttpPost("customers")]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerAccountRequest request, CancellationToken ct) => await WithActor(actor => service.CreateCustomerAccountAsync(request, actor, ct));
    [HttpPost("customer-invoices")]
    public async Task<IActionResult> CreateCustomerInvoice([FromBody] CreateCustomerInvoiceRequest request, CancellationToken ct) => await WithActor(actor => service.CreateCustomerInvoiceAsync(request, actor, ct));
    [HttpPost("customer-invoices/{id:guid}/issue")]
    public async Task<IActionResult> IssueCustomerInvoice(Guid id, [FromBody] IssueCustomerInvoiceRequest request, CancellationToken ct) => await WithActor(actor => service.IssueCustomerInvoiceAsync(id, request, actor, ct));
    [HttpPost("customer-receipts")]
    public async Task<IActionResult> RecordCustomerReceipt([FromBody] RecordCustomerReceiptRequest request, CancellationToken ct) => await WithActor(actor => service.RecordCustomerReceiptAsync(request, actor, ct));
    [HttpPost("customer-receipts/{id:guid}/allocations")]
    public async Task<IActionResult> AllocateCustomerReceipt(Guid id, [FromBody] AllocateCustomerReceiptRequest request, CancellationToken ct) => await WithActor(actor => service.AllocateCustomerReceiptAsync(id, request, actor, ct));

    [HttpPost("employee-pay-contracts")]
    public async Task<IActionResult> CreateEmployeePayContract([FromBody] CreateEmployeePayContractRequest request, CancellationToken ct) => await WithActor(actor => service.CreateEmployeePayContractAsync(request, actor, ct));
    [HttpPost("payroll-runs")]
    public async Task<IActionResult> CreatePayrollRun([FromBody] CreatePayrollRunRequest request, CancellationToken ct) => await WithActor(actor => service.CreatePayrollRunAsync(request, actor, ct));
    [HttpPost("payroll-runs/{id:guid}/prepare")]
    public async Task<IActionResult> PreparePayrollRun(Guid id, [FromBody] PreparePayrollRunRequest request, CancellationToken ct) => await WithActor(actor => service.PreparePayrollRunAsync(id, request, actor, ct));
    [HttpPost("payroll-runs/{id:guid}/pay")]
    public async Task<IActionResult> PayPayrollRun(Guid id, [FromBody] PayPayrollRunRequest request, CancellationToken ct) => await WithActor(actor => service.PayPayrollRunAsync(id, request, actor, ct));

    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierAccountRequest request, CancellationToken ct) => await WithActor(actor => service.CreateSupplierAccountAsync(request, actor, ct));
    [HttpPost("supplier-invoices")]
    public async Task<IActionResult> CreateSupplierInvoice([FromBody] CreateSupplierInvoiceRequest request, CancellationToken ct) => await WithActor(actor => service.CreateSupplierInvoiceAsync(request, actor, ct));
    [HttpPost("supplier-invoices/{id:guid}/record")]
    public async Task<IActionResult> RecordSupplierInvoice(Guid id, [FromBody] RecordSupplierInvoiceRequest request, CancellationToken ct) => await WithActor(actor => service.RecordSupplierInvoiceAsync(id, request, actor, ct));
    [HttpPost("supplier-payments")]
    public async Task<IActionResult> RecordSupplierPayment([FromBody] RecordSupplierPaymentRequest request, CancellationToken ct) => await WithActor(actor => service.RecordSupplierPaymentAsync(request, actor, ct));
    [HttpPost("supplier-payments/{id:guid}/allocations")]
    public async Task<IActionResult> AllocateSupplierPayment(Guid id, [FromBody] AllocateSupplierPaymentRequest request, CancellationToken ct) => await WithActor(actor => service.AllocateSupplierPaymentAsync(id, request, actor, ct));

    [HttpPost("inventory-items")]
    public async Task<IActionResult> CreateInventoryItem([FromBody] CreateInventoryItemRequest request, CancellationToken ct) => await WithActor(actor => service.CreateInventoryItemAsync(request, actor, ct));
    [HttpPost("inventory-movements")]
    public async Task<IActionResult> RecordInventoryMovement([FromBody] RecordInventoryMovementRequest request, CancellationToken ct) => await WithActor(actor => service.RecordInventoryMovementAsync(request, actor, ct));
    [HttpPost("expense-claims")]
    public async Task<IActionResult> CreateExpenseClaim([FromBody] CreateExpenseClaimRequest request, CancellationToken ct) => await WithActor(actor => service.CreateExpenseClaimAsync(request, actor, ct));
    [HttpPost("bank-accounts")]
    public async Task<IActionResult> CreateBankAccount([FromBody] CreateBankAccountRequest request, CancellationToken ct) => await WithActor(actor => service.CreateBankAccountAsync(request, actor, ct));
    [HttpPost("bank-statement-lines")]
    public async Task<IActionResult> RecordBankStatementLine([FromBody] RecordBankStatementLineRequest request, CancellationToken ct) => await WithActor(actor => service.RecordBankStatementLineAsync(request, actor, ct));
    [HttpPost("bank-statement-lines/{id:guid}/reconcile")]
    public async Task<IActionResult> ReconcileBankStatementLine(Guid id, [FromBody] ReconcileBankStatementLineRequest request, CancellationToken ct) => await WithActor(actor => service.ReconcileBankStatementLineAsync(id, request, actor, ct));

    [HttpPost("tax-codes")]
    public async Task<IActionResult> CreateTaxCode([FromBody] CreateTaxCodeRequest request, CancellationToken ct) => await WithActor(actor => service.CreateTaxCodeAsync(request, actor, ct));
    [HttpPost("tax-returns")]
    public async Task<IActionResult> PrepareTaxReturn([FromBody] PrepareTaxReturnRequest request, CancellationToken ct) => await WithActor(actor => service.PrepareTaxReturnAsync(request, actor, ct));
    [HttpPost("tax-returns/{id:guid}/submit")]
    public async Task<IActionResult> SubmitTaxReturn(Guid id, [FromBody] SubmitTaxReturnRequest request, CancellationToken ct) => await WithActor(actor => service.SubmitTaxReturnAsync(id, request, actor, ct));
    [HttpPost("fixed-assets")]
    public async Task<IActionResult> CreateFixedAsset([FromBody] CreateFixedAssetRequest request, CancellationToken ct) => await WithActor(actor => service.CreateFixedAssetAsync(request, actor, ct));
    [HttpPost("budgets")]
    public async Task<IActionResult> CreateBudget([FromBody] CreateBudgetRequest request, CancellationToken ct) => await WithActor(actor => service.CreateBudgetAsync(request, actor, ct));

    private async Task<IActionResult> WithActor<T>(Func<string, Task<Application.Abstraction.Result<T>>> action)
    { var actor = User.GetUserId(); return string.IsNullOrWhiteSpace(actor) ? Unauthorized() : ToAction(await action(actor)); }
    private async Task<IActionResult> WithActor(Func<string, Task<Application.Abstraction.Result>> action)
    { var actor = User.GetUserId(); return string.IsNullOrWhiteSpace(actor) ? Unauthorized() : ToAction(await action(actor)); }
    private IActionResult ToAction<T>(Application.Abstraction.Result<T> result) => result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    private IActionResult ToAction(Application.Abstraction.Result result) => result.IsSuccess ? Ok() : result.ToProblem();
}
