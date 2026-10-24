using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Strangler.Common;

namespace Strangler.Monolith.Pages;

public sealed class RequisitionsModel(Requisitions requisitions) : PageModel
{
    [BindProperty]
    public string? Title { get; set; }
    [BindProperty]
    public string? Reason { get; set; }
    [BindProperty]
    public Guid RequestId { get; set; } = Guid.NewGuid();
    [BindProperty]
    public Guid Id { get; set; }
    [BindProperty]
    public int Version { get; set; }
    [BindProperty]
    public string? Decision { get; set; }
    [BindProperty]
    public string? DecisionReason { get; set; }

    public PageResult<Requisition> Requisitions { get; private set; } = new([], null);
    public Requisition? Selected { get; private set; }
    public IReadOnlyList<AuditEntry> Audit { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, Guid? after, CancellationToken cancellationToken)
    {
        Requisitions = await requisitions.ListAsync(User.Owner(), after, 20, cancellationToken);
        if (id is not null)
        {
            Selected = await requisitions.GetAsync(User.Owner(), id.Value, cancellationToken);
            if (Selected is null) { return NotFound(); }
            Audit = await requisitions.AuditAsync(User.Owner(), id.Value, cancellationToken);
            Title = Selected.Title;
            Reason = Selected.Reason;
            Id = Selected.Id;
            Version = Selected.Version;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("requester")) { return Forbid(); }
        var title = InputRules.Title(Title);
        var reason = InputRules.Reason(Reason);
        if (!ModelState.IsValid || title is null || reason is null || RequestId == Guid.Empty)
        {
            return await FailureAsync("Enter a title and business reason within the field limits.", 400, cancellationToken);
        }

        var item = await requisitions.CreateAsync(User.Owner(), RequestId, title, reason, cancellationToken);
        return item is not null ? LocalRedirect($"/Requisitions?id={item.Id}")
            : await FailureAsync("This request key was already used. Reload before creating another request.", 409, cancellationToken);
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("requester")) { return Forbid(); }
        var title = InputRules.Title(Title);
        var reason = InputRules.Reason(Reason);
        if (!ValidVersion() || title is null || reason is null)
        {
            return await FailureAsync("Check the title and business reason, then reload if the version is missing.", 400, cancellationToken);
        }

        return await ChangedAsync(await requisitions.UpdateDraftAsync(User.Owner(), Id, title, reason, Version, cancellationToken), cancellationToken);
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("requester")) { return Forbid(); }
        var title = InputRules.Title(Title);
        var reason = InputRules.Reason(Reason);
        if (!ValidVersion() || title is null || reason is null)
        {
            return await FailureAsync("Check the title and business reason before submitting.", 400, cancellationToken);
        }

        var saved = await requisitions.GetAsync(User.Owner(), Id, cancellationToken);
        if (saved is null || saved.RequesterId != User.Owner()) { return NotFound(); }
        if (saved.Version != Version || saved.Title != title || saved.Reason != reason)
        {
            return await FailureAsync("Save changes before submitting. Your draft has not been submitted.", 409, cancellationToken);
        }

        return await ChangedAsync(await requisitions.TransitionAsync(User.Owner(), Id, false, "submitted", null, Version, cancellationToken), cancellationToken);
    }

    public async Task<IActionResult> OnPostDecideAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("approver")) { return Forbid(); }
        var reason = DecisionReason is null ? null : InputRules.Reason(DecisionReason);
        if (!ValidVersion() || Decision is not ("approved" or "rejected") ||
            (Decision == "rejected" && reason is null) || (DecisionReason is not null && reason is null))
        {
            return await FailureAsync("Choose approve or reject. Rejection requires a reason.", 400, cancellationToken);
        }

        return await ChangedAsync(await requisitions.TransitionAsync(User.Owner(), Id, true, Decision, reason, Version, cancellationToken), cancellationToken);
    }

    private bool ValidVersion() => ModelState.IsValid && Id != Guid.Empty && Version is > 0 and < int.MaxValue;

    private async Task<IActionResult> ChangedAsync(ChangeResult result, CancellationToken cancellationToken)
    {
        if (result.Item is not null) { return LocalRedirect($"/Requisitions?id={result.Item.Id}"); }
        return result.Error == "not_found" ? NotFound()
            : await FailureAsync("The request changed or cannot make that transition. Reload it before trying again.", 409, cancellationToken);
    }

    private async Task<IActionResult> FailureAsync(string message, int status, CancellationToken cancellationToken)
    {
        ErrorMessage = message;
        Response.StatusCode = status;
        Requisitions = await requisitions.ListAsync(User.Owner(), null, 20, cancellationToken);
        if (Id != Guid.Empty)
        {
            Selected = await requisitions.GetAsync(User.Owner(), Id, cancellationToken);
            Audit = await requisitions.AuditAsync(User.Owner(), Id, cancellationToken);
        }

        return Page();
    }
}
