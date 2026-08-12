namespace Okojo.Globalization;

/// <summary>Portable Intl formatting part produced by a formatter core.</summary>
public readonly record struct IntlPart(string Type, string Value, string? Unit = null);
