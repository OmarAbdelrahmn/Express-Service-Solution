namespace Application.Abstraction;

public class Result
{
    public Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.non || !isSuccess && error == Error.non)
            throw new InvalidOperationException("Invalid Operation");

        IsSuccess = isSuccess;
        Error = error;
    }
    public bool IsSuccess { get; set; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; } = default!;

    public static Result Success() => new(true, Error.non);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.non);
    public static Result<TValue> Failure<TValue>(Error error) => new(default!, false, error);
}
public class Result<TValue>(TValue value, bool isSuccess, Error error) : Result(isSuccess, error)
{
    private readonly TValue? _value = value;

    public TValue Value => IsSuccess ?
        _value! :
        throw new InvalidOperationException("Failure operation can't have value");

}