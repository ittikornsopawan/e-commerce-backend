using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Tests.Authen;

// Spec 01-register, sequence "Validate submitted ids/versions and all applicable required acceptances" (R12-R16).
public sealed class RegistrationConsentPolicyTests
{
    private static readonly Guid Required = Guid.NewGuid();
    private static readonly Guid Optional = Guid.NewGuid();

    private static readonly ApplicableConsent[] Applicable =
    [
        new(Required, "1.0.0", IsRequired: true),
        new(Optional, "1.0.0", IsRequired: false)
    ];

    private static Result Check(bool referrerFound = true, params SubmittedConsent[] submitted) =>
        RegistrationConsentPolicy.Check(Applicable, submitted, referrerFound);

    [Fact]
    public void TC_REG_001_accepted_required_and_answered_optional_pass() =>
        Assert.True(Check(true, new(Required, "1.0.0", true), new(Optional, "1.0.0", false)).IsSuccess);

    [Fact]
    public void TC_REG_013_a_missing_referrer_is_404_E10005()
    {
        var error = Check(false, new SubmittedConsent(Required, "1.0.0", true)).FirstError;
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("E10005", error.BizCode);
        Assert.Equal("Consent or referrer not found.", error.Description);
    }

    [Fact]
    public void TC_REG_014_a_consent_that_is_not_applicable_is_404_E10005() =>
        Assert.Equal(AuthenErrors.ConsentOrReferrerNotFound,
            Check(true, new(Required, "1.0.0", true), new(Guid.NewGuid(), "1.0.0", true)).FirstError);

    [Fact]
    public void TC_REG_015_a_stale_version_is_400_and_comes_after_the_404()
    {
        Assert.Equal(AuthenErrors.ConsentVersionNotCurrent, Check(true, new SubmittedConsent(Required, "0.9.0", true)).FirstError);
        Assert.Equal(AuthenErrors.ConsentOrReferrerNotFound, Check(false, new SubmittedConsent(Required, "0.9.0", true)).FirstError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void TC_REG_016_a_required_consent_needs_an_explicit_true(bool? result) =>
        Assert.Equal(AuthenErrors.RequiredConsentNotAccepted, Check(true, new SubmittedConsent(Required, "1.0.0", result)).FirstError);

    [Fact]
    public void TC_REG_016_an_omitted_required_consent_is_rejected() =>
        Assert.Equal(AuthenErrors.RequiredConsentNotAccepted, Check(true).FirstError);

    [Fact]
    public void TC_REG_018_no_applicable_required_consent_allows_no_consents() =>
        Assert.True(RegistrationConsentPolicy.Check([new ApplicableConsent(Optional, "1.0.0", false)], [], true).IsSuccess);

    [Fact]
    public void TC_REG_004_password_failures_use_the_spec_message()
    {
        const string message = "Password must be 8 to 128 characters and contain an upper-case letter, a lower-case letter and a digit.";
        Assert.Equal(message, PlainPassword.Create("Passw0r").FirstError.Description);
        Assert.Equal(message, PlainPassword.Create("password1").FirstError.Description);
    }
}
