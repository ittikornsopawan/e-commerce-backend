namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>A consent that applies now: effective, ACTIVE and of an ACTIVE type. <c>IsRequired</c> is the consent's own flag.</summary>
public sealed record ApplicableConsent(Guid ConsentId, string Version, bool IsRequired);

/// <summary>A consent the user saw: the displayed version and the answer (<c>null</c> = read only).</summary>
public sealed record SubmittedConsent(Guid ConsentId, string Version, bool? Result);
