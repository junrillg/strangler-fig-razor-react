using Strangler.Common;

namespace Strangler.Monolith;

public static class FeatureEndpoints
{
    public static void MapFeatures(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<CsrfFilter>();
        api.MapGet("/requisitions", async (HttpContext context, Requisitions requisitions, Guid? after, int? limit, CancellationToken cancellationToken) =>
        {
            if (limit is < 1 or > 50) { return Results.BadRequest(new { error = "invalid_limit" }); }
            return Results.Ok(await requisitions.ListAsync(context.User.Owner(), after, limit ?? 20, cancellationToken));
        }).Produces<PageResult<Requisition>>();
        api.MapGet("/requisitions/{id:guid}", async (HttpContext context, Requisitions requisitions, Guid id, CancellationToken cancellationToken) =>
        {
            var item = await requisitions.GetAsync(context.User.Owner(), id, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }).Produces<Requisition>();
        api.MapPost("/requisitions", async (HttpContext context, Requisitions requisitions, RequisitionCreate input, CancellationToken cancellationToken) =>
        {
            if (!context.User.IsInRole("requester")) { return Results.Forbid(); }
            var title = InputRules.Title(input.Title);
            var reason = InputRules.Reason(input.Reason);
            if (input.RequestId == Guid.Empty || title is null || reason is null)
            {
                return Results.BadRequest(new { error = "invalid_requisition" });
            }

            var item = await requisitions.CreateAsync(context.User.Owner(), input.RequestId, title, reason, cancellationToken);
            return item is null ? Results.Conflict(new { error = "idempotency_conflict" })
                : Results.Created($"/api/requisitions/{item.Id}", item);
        }).Produces<Requisition>(StatusCodes.Status201Created);
        api.MapPut("/requisitions/{id:guid}", async (HttpContext context, Requisitions requisitions, Guid id, RequisitionUpdate input, CancellationToken cancellationToken) =>
        {
            if (!context.User.IsInRole("requester")) { return Results.Forbid(); }
            var title = InputRules.Title(input.Title);
            var reason = InputRules.Reason(input.Reason);
            if (input.Version < 1 || input.Version == int.MaxValue || title is null || reason is null)
            {
                return Results.BadRequest(new { error = "invalid_requisition" });
            }

            return Changed(await requisitions.UpdateDraftAsync(context.User.Owner(), id, title, reason, input.Version, cancellationToken));
        }).Produces<Requisition>();
        api.MapPost("/requisitions/{id:guid}/submit", async (HttpContext context, Requisitions requisitions, Guid id, VersionInput input, CancellationToken cancellationToken) =>
        {
            if (!context.User.IsInRole("requester")) { return Results.Forbid(); }
            if (input.Version < 1 || input.Version == int.MaxValue) { return Results.BadRequest(new { error = "invalid_version" }); }
            return Changed(await requisitions.TransitionAsync(context.User.Owner(), id, false, "submitted", null, input.Version, cancellationToken));
        }).Produces<Requisition>();
        api.MapPost("/requisitions/{id:guid}/decision", async (HttpContext context, Requisitions requisitions, Guid id, DecisionInput input, CancellationToken cancellationToken) =>
        {
            if (!context.User.IsInRole("approver")) { return Results.Forbid(); }
            var reason = input.Reason is null ? null : InputRules.Reason(input.Reason);
            if (input.Version < 1 || input.Version == int.MaxValue || input.Status is not ("approved" or "rejected") ||
                (input.Status == "rejected" && reason is null) || (input.Reason is not null && reason is null))
            {
                return Results.BadRequest(new { error = "invalid_decision" });
            }

            return Changed(await requisitions.TransitionAsync(context.User.Owner(), id, true, input.Status, reason, input.Version, cancellationToken));
        }).Produces<Requisition>();
        api.MapGet("/requisitions/{id:guid}/audit", async (HttpContext context, Requisitions requisitions, Guid id, CancellationToken cancellationToken) =>
        {
            if (await requisitions.GetAsync(context.User.Owner(), id, cancellationToken) is null) { return Results.NotFound(); }
            return Results.Ok(await requisitions.AuditAsync(context.User.Owner(), id, cancellationToken));
        }).Produces<IReadOnlyList<AuditEntry>>();
        api.MapGet("/notes", async (HttpContext context, Notes notes, Guid? after, int? limit, CancellationToken cancellationToken) =>
        {
            if (limit is < 1 or > 50) { return Results.BadRequest(new { error = "invalid_limit" }); }
            return Results.Ok(await notes.ListAsync(context.User.Owner(), after, limit ?? 20, cancellationToken));
        }).Produces<PageResult<Note>>();
        api.MapPost("/notes", async (HttpContext context, Notes notes, NoteCreate input, CancellationToken cancellationToken) =>
        {
            var body = InputRules.NoteBody(input.Body);
            if (input.RequestId == Guid.Empty || body is null) { return Results.BadRequest(new { error = "invalid_note" }); }
            var note = await notes.CreateAsync(context.User.Owner(), input.RequestId, body, cancellationToken);
            return note is null ? Results.Conflict(new { error = "idempotency_conflict" }) : Results.Created("/Notes", note);
        }).Produces<Note>(StatusCodes.Status201Created);
    }

    private static IResult Changed(ChangeResult result) => result.Item is not null ? Results.Ok(result.Item)
        : result.Error == "not_found" ? Results.NotFound() : Results.Conflict(new { error = result.Error });
}
