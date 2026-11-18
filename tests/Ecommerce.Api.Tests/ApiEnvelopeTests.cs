using Ecommerce.Api.Contracts;
using Ecommerce.Shared.Results;

namespace Ecommerce.Api.Tests;

// Spec: api-conventions.md "Error codes" and the response examples of 01-register (status object).
public sealed class ApiEnvelopeTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 42, 0, 123, DateTimeKind.Utc);

    [Fact]
    public void Success_uses_the_spec_status_object_and_S10000()
    {
        var envelope = ApiEnvelope.Success(201, "Created", "Register Success", "req-1", Now, new { userId = "u" });

        Assert.Equal(201, envelope.Status.Code);
        Assert.Equal("Created", envelope.Status.Message);
        Assert.Equal("S10000", envelope.Status.BizCode);
        Assert.Equal("Register Success", envelope.Status.BizMessage);
        Assert.Equal("req-1", envelope.Status.RequestId);
        Assert.Equal("2026-11-05T12:42:00.123Z", envelope.Status.Timestamp);
        Assert.Equal("SUCCESS", envelope.Status.Status);
        Assert.NotNull(envelope.Data);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400, "Bad Request", "E10001")]
    [InlineData(ErrorType.Unauthorized, 401, "Unauthorized", "E10002")]
    [InlineData(ErrorType.Forbidden, 403, "Forbidden", "E10009")]
    [InlineData(ErrorType.NotFound, 404, "Not Found", "E10004")]
    [InlineData(ErrorType.Conflict, 409, "Conflict", "E10003")]
    [InlineData(ErrorType.Timeout, 408, "Request Timeout", "E90001")]
    [InlineData(ErrorType.TooManyRequests, 429, "Too Many Requests", "E90002")]
    public void Failure_maps_the_error_type_to_one_http_status_and_bizCode(
        ErrorType type, int http, string message, string bizCode)
    {
        var envelope = ApiEnvelope.Failure(new Error("X", "desc", type), "req-2", Now);

        Assert.Equal(http, envelope.Status.Code);
        Assert.Equal(message, envelope.Status.Message);
        Assert.Equal(bizCode, envelope.Status.BizCode);
        Assert.Equal("desc", envelope.Status.BizMessage);
        Assert.Equal("ERROR", envelope.Status.Status);
        Assert.Null(envelope.Data);
    }

    [Theory]
    [InlineData(ErrorType.NotFound, "E10005", 404)]
    [InlineData(ErrorType.Conflict, "E10006", 409)]
    [InlineData(ErrorType.Conflict, "E10007", 409)]
    [InlineData(ErrorType.Conflict, "E10008", 409)]
    public void An_explicit_bizCode_overrides_the_default_of_its_error_type(ErrorType type, string bizCode, int http)
    {
        var envelope = ApiEnvelope.Failure(Error.Of("X", "d", type, bizCode), "req-3", Now);

        Assert.Equal(bizCode, envelope.Status.BizCode);
        Assert.Equal(http, envelope.Status.Code);
    }

    [Theory]
    [InlineData(ErrorType.Conflict, "E10005")]
    [InlineData(ErrorType.NotFound, "E10006")]
    [InlineData(ErrorType.Validation, "E10004")]
    [InlineData(ErrorType.Failure, "E90001")]
    public void An_explicit_bizCode_on_the_wrong_error_type_is_rejected(ErrorType type, string bizCode)
    {
        Assert.Throws<ArgumentException>(() => Error.Of("X", "d", type, bizCode));
    }

    [Fact]
    public void Success_message_is_Ok_as_in_every_spec()
    {
        Assert.Equal("Ok", ApiEnvelope.Success(200, "Ok", "Success", "r", Now, (object?)null).Status.Message);
    }

    [Fact]
    public void An_unclassified_failure_is_500_without_a_bizCode_and_hides_the_description()
    {
        var envelope = ApiEnvelope.Failure(Error.Failure("Boom", "stack trace here"), "req-4", Now);

        Assert.Equal(500, envelope.Status.Code);
        Assert.Null(envelope.Status.BizCode);
        Assert.Equal("An unexpected error occurred.", envelope.Status.BizMessage);
    }
}
