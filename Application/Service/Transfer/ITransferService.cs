using Application.Abstraction;
using Application.Contracts.SupplierCon;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Transfer;

public interface ITransferService
{
    Task<Result<TransferResponse>> TransferToHousingAsync(TransferRequest request, string transferredBy);
    Task<Result<IEnumerable<TransferResponse>>> GetAllTransfersAsync();
    Task<Result<TransferResponse>> GetTransferByIdAsync(int id);
    Task<Result<IEnumerable<TransferResponse>>> GetTransfersByHousingAsync(int housingId);
}