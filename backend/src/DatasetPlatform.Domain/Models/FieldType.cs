namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Defines the data type of a <see cref="SchemaField"/>.
/// The type controls how the value is validated and how it is rendered in the UI.
/// </summary>
public enum FieldType
{
    /// <summary>Free-form text. May be constrained by <see cref="SchemaField.MaxLength"/>.</summary>
    String = 1,

    /// <summary>Numeric value (integer or decimal). May be bounded by <see cref="SchemaField.MinValue"/> and <see cref="SchemaField.MaxValue"/>.</summary>
    Number = 2,

    /// <summary>Calendar date (no time component). Expected format: yyyy-MM-dd.</summary>
    Date = 3,

    /// <summary>True/false toggle value.</summary>
    Boolean = 4,

    /// <summary>
    /// Value must be one of the entries in <see cref="SchemaField.AllowedValues"/>.
    /// Renders as a drop-down in the UI.
    /// </summary>
    Select = 5,

    /// <summary>
    /// Value is looked up from another dataset identified by <see cref="SchemaField.LookupDatasetKey"/>.
    /// The permissible values are fetched at runtime via the /api/lookups endpoint.
    /// </summary>
    Lookup = 6
}
