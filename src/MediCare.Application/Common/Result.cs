namespace MediCare.Application.Common;

/// <summary>
/// Lightweight result object used by application use cases instead of throwing for
/// expected failures. Controllers translate failures into ModelState errors.
/// </summary>
public class Result
{
    protected Result(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? Error => Errors.Count > 0 ? Errors[0] : null;

    public static Result Ok() => new(true, Array.Empty<string>());

    public static Result Fail(params string[] errors) =>
        new(false, errors.Length == 0 ? new[] { "The operation failed." } : errors);

    public static Result Fail(IEnumerable<string> errors)
    {
        var list = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
        return new Result(false, list.Length == 0 ? new[] { "The operation failed." } : list);
    }

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    public static Result<T> Fail<T>(params string[] errors) => Result<T>.Fail(errors);

    public static Result<T> Fail<T>(IEnumerable<string> errors) => Result<T>.Fail(errors);
}

public sealed class Result<T> : Result
{
    private Result(bool succeeded, T? value, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Ok(T value) => new(true, value, Array.Empty<string>());

    public static new Result<T> Fail(params string[] errors) =>
        new(false, default, errors.Length == 0 ? new[] { "The operation failed." } : errors);

    public static new Result<T> Fail(IEnumerable<string> errors)
    {
        var list = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
        return new Result<T>(false, default, list.Length == 0 ? new[] { "The operation failed." } : list);
    }
}
