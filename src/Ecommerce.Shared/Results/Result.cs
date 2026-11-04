namespace Ecommerce.Shared.Results;

public interface IFailureFactory<TSelf> where TSelf : IFailureFactory<TSelf>
{
    static abstract TSelf Failure(IReadOnlyList<Error> errors);
}

public class Result : IFailureFactory<Result>
{
    protected Result(IReadOnlyList<Error> errors)
    {
        Errors = errors;
    }

    public IReadOnlyList<Error> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public bool IsFailure => !IsSuccess;

    public Error FirstError => Errors.Count > 0
        ? Errors[0]
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new([]);

    public static Result Failure(Error error) => new([error]);

    public static Result Failure(IReadOnlyList<Error> errors) =>
        errors.Count == 0
            ? throw new ArgumentException("A failure needs at least one error.", nameof(errors))
            : new Result(errors);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure([error]);
}

public sealed class Result<T> : Result, IFailureFactory<Result<T>>
{
    private readonly T? _value;

    private Result(T? value, IReadOnlyList<Error> errors) : base(errors)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, []);

    public new static Result<T> Failure(IReadOnlyList<Error> errors) =>
        errors.Count == 0
            ? throw new ArgumentException("A failure needs at least one error.", nameof(errors))
            : new Result<T>(default, errors);

    public static implicit operator Result<T>(Error error) => Failure([error]);

    public static implicit operator Result<T>(T value) => Success(value);
}
