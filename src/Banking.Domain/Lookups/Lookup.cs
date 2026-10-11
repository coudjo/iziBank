namespace Banking.Domain.Lookups;

/// <summary>
/// Standard lookup shape: <c>Id</c>, unique <c>Code</c>, <c>Name</c>, <c>IsActive</c>.
/// Tables that do not match this shape (for example <c>EventType</c>) do not inherit this type.
/// </summary>
public abstract class Lookup
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
