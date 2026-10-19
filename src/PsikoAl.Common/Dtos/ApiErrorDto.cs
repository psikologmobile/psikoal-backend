using System.Text.Json.Serialization;

namespace PsikoAl.Common.Dtos;

public sealed record ApiErrorDto(
    string Code,
    string Message,
    string? Field = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Meta = null);
