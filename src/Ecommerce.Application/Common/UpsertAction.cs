namespace Ecommerce.Application.Common;

/// <summary>
/// What an upsert did (specs 09 and the author upserts): Insert answers 201, Update and Archive 200, Delete 204 with no
/// body. Archive is an update to ARCHIVED, which the author specs answer with their own success message.
/// </summary>
public enum UpsertAction
{
    Insert,
    Update,
    Archive,
    Delete
}
