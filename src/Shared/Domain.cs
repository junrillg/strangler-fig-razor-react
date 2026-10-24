using System.ComponentModel.DataAnnotations;

namespace Strangler.Common;

public sealed record Requisition(Guid Id, Guid RequesterId, Guid ApproverId, string Title, string Reason,
    string Status, int Version, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record AuditEntry(Guid Id, Guid ActorId, string? FromStatus, string ToStatus, int Version,
    string? Reason, DateTime OccurredAt);
public sealed record Note(Guid Id, string Body, DateTime CreatedAt);
public sealed record RequisitionCreate(Guid RequestId,
    [property: Required, StringLength(120, MinimumLength = 1)] string? Title,
    [property: Required, StringLength(1000, MinimumLength = 1)] string? Reason);
public sealed record RequisitionUpdate(
    [property: Required, StringLength(120, MinimumLength = 1)] string? Title,
    [property: Required, StringLength(1000, MinimumLength = 1)] string? Reason,
    [property: Range(1, int.MaxValue - 1)] int Version);
public sealed record VersionInput([property: Range(1, int.MaxValue - 1)] int Version);
public sealed record DecisionInput([property: Range(1, int.MaxValue - 1)] int Version,
    [property: Required, RegularExpression("^(approved|rejected)$")] string? Status,
    [property: StringLength(1000, MinimumLength = 1)] string? Reason);
public sealed record NoteCreate(Guid RequestId, string? Body);
public sealed record PageResult<T>(IReadOnlyList<T> Items, Guid? NextCursor);
public sealed record ChangeResult(Requisition? Item, string? Error);

public static class InputRules
{
    public static string? Title(string? value) => Text(value, 120);
    public static string? Reason(string? value) => Text(value, 1000);
    public static string? NoteBody(string? value) => Text(value, 2000);

    private static string? Text(string? value, int maximum)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > maximum ||
            trimmed.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')
            ? null : trimmed;
    }
}
