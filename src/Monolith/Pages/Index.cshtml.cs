using Microsoft.AspNetCore.Mvc.RazorPages;
using Strangler.Common;

namespace Strangler.Monolith.Pages;

public sealed class IndexModel(Requisitions requisitions, Notes notes) : PageModel
{
    public IReadOnlyList<Requisition> Requisitions { get; private set; } = [];
    public IReadOnlyList<Note> Notes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Requisitions = (await requisitions.ListAsync(User.Owner(), null, 5, cancellationToken)).Items;
        Notes = (await notes.ListAsync(User.Owner(), null, 3, cancellationToken)).Items;
    }
}
