using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using FluentValidation;
using MediatR;

namespace laundryghar.Warehouse.Application.Common;

public sealed class ValidationPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> v) => _validators = v;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any()) return await next();

        var failures = _validators
            .Select(v => v.Validate(new ValidationContext<TRequest>(request)))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        if (failures.Count > 0)
            throw new laundryghar.Utilities.Exceptions.ValidationException(failures);

        return await next();
    }
}
