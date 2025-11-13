using Application.Abstraction;
using Application.Contracts.Auth;

namespace Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> SingInAsync(AuthRequest request);
    Task<Result> RegisterAsync(RegisterRequest request);
    Task<Result> AdminRegisterAsync(RegisterRequest request);
    Task<Result> MasterRegisterAsync(RegisterRequest request);
}
