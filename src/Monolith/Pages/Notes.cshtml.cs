using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Strangler.Common;

namespace Strangler.Monolith.Pages;

public sealed class NotesModel(Notes notes) : PageModel
{
    [BindProperty]
    public string Body { get; set; } = "";
    [BindProperty]
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public PageResult<Note> Notes { get; private set; } = new([], null);
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(Guid? after, CancellationToken cancellationToken) =>
        Notes = await notes.ListAsync(User.Owner(), after, 20, cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var body = InputRules.NoteBody(Body);
        if (!ModelState.IsValid || body is null || RequestId == Guid.Empty)
        {
            ErrorMessage = "Enter a note between 1 and 2,000 characters.";
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        else if (await notes.CreateAsync(User.Owner(), RequestId, body, cancellationToken) is not null)
        {
            return LocalRedirect("/Notes");
        }
        else
        {
            ErrorMessage = "This note request was already used. Reload before starting another note.";
            Response.StatusCode = StatusCodes.Status409Conflict;
        }

        await OnGetAsync(null, cancellationToken);
        return Page();
    }
}
