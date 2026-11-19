namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>The text of one user address (spec 09 body); storage encrypts every field except the title.</summary>
public sealed record AddressValues(string Title, string Address, string City, string State, string PostalCode, string Country);

/// <summary>An owned, non-DELETED address found by its id.</summary>
public sealed record OwnedAddress(Guid Id, bool IsDefault);
