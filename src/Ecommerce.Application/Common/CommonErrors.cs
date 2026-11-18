using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Common;

/// <summary>Errors every use case can answer (api-conventions.md "Error codes").</summary>
public static class CommonErrors
{
    public static readonly Error Timeout =
        Error.Of("Common.Timeout", "The server timed out waiting for the request.", ErrorType.Timeout, "E90001");
}
