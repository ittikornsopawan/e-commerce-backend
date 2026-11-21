namespace Ecommerce.Domain.Author.Repositories;

/// <summary>
/// The filters of an author master inquiry (role 03, permission 06, scope 09), the identical predicates of the count and
/// the page; a null filter is disabled (typed SQL NULL). SearchPattern is one bound ILIKE pattern on name or description.
/// </summary>
public sealed record MasterFilter(Guid? Id, string? SearchPattern, string? RowStatus);
