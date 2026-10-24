using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Security.Claims;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Strangler.Common;

namespace Strangler.BackendTests;

public sealed class SessionTests : IDisposable
{
    private readonly string keyPath = Path.Combine(Path.GetTempPath(), "strangler-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;

    public SessionTests()
    {
        var host = Environment.GetEnvironmentVariable("TEST_DATABASE_HOST")
            ?? throw new InvalidOperationException("TEST_DATABASE_HOST is required; integration tests never use a fake database.");
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = host,
                ["Database:Name"] = Environment.GetEnvironmentVariable("TEST_DATABASE_NAME") ?? "strangler",
                ["Database:Username"] = Environment.GetEnvironmentVariable("TEST_DATABASE_USERNAME") ?? "monolith_app",
                ["Database:PasswordFile"] = Environment.GetEnvironmentVariable("TEST_DATABASE_PASSWORD_FILE") ?? "/run/secrets/monolith_db_password",
                ["Auth:KeyRingPath"] = keyPath,
                ["Auth:AllowInsecureHttp"] = "false",
            }));
        });
    }

    [Fact]
    public async Task AnonymousAccessAndLoginWithoutAntiforgeryAreRejected()
    {
        using var client = CreateClient();
        using var session = await client.GetAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/session", new LoginInput("alex", Password("alex")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task AuthenticationRequiresRealCredentialsAndLogoutRemovesBrowserSession()
    {
        using var client = CreateClient();
        await AntiforgeryAsync(client);
        using var rejected = await client.PostAsJsonAsync("/api/session", new LoginInput("alex", "synthetic-invalid-password"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        await LoginAsync(client, "alex");
        var session = await client.GetFromJsonAsync<Session>("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal("alex", session?.Name);
        using var logout = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await client.GetAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task AnonymousTokenCannotBeReusedAfterLoginAndUnknownFieldsAreRejected()
    {
        using var client = CreateClient();
        await AntiforgeryAsync(client);
        using var rejected = await client.PostAsJsonAsync("/api/session", new
        {
            username = "alex",
            password = Password("alex"),
            ownerId = Guid.NewGuid(),
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/session", new LoginInput("alex", Password("alex")), TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        using var stale = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        await AntiforgeryAsync(client);
        using var current = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task OversizedPayloadIsRejectedAndReadinessUsesRealPostgres()
    {
        using var client = CreateClient();
        using var content = new StringContent(JsonSerializer.Serialize(new { username = "alex", password = new string('x', 17_000) }), Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/api/session", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        Assert.Equal("no-store", rejected.Headers.CacheControl?.ToString());
        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        readiness.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task RequisitionPreservesDraftThenSubmitThenApprovalAndAtomicAudit()
    {
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        var draft = await CreateRequisitionAsync(requester);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(1, draft.Version);
        using var changedResponse = await requester.PutAsJsonAsync($"/api/requisitions/{draft.Id}",
            new RequisitionUpdate("Synthetic revised role", "Synthetic revised reason", draft.Version), TestContext.Current.CancellationToken);
        changedResponse.EnsureSuccessStatusCode();
        var changed = await changedResponse.Content.ReadFromJsonAsync<Requisition>(TestContext.Current.CancellationToken);
        Assert.NotNull(changed);
        Assert.Equal(2, changed.Version);
        using var submittedResponse = await requester.PostAsJsonAsync($"/api/requisitions/{draft.Id}/submit", new VersionInput(changed.Version), TestContext.Current.CancellationToken);
        submittedResponse.EnsureSuccessStatusCode();
        var submitted = await submittedResponse.Content.ReadFromJsonAsync<Requisition>(TestContext.Current.CancellationToken);
        Assert.NotNull(submitted);
        Assert.Equal("submitted", submitted.Status);
        using var frozen = await requester.PutAsJsonAsync($"/api/requisitions/{draft.Id}",
            new RequisitionUpdate("Forbidden edit", "Submitted drafts are frozen", submitted.Version), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        using var roleDenied = await requester.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(submitted.Version, "approved", null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, roleDenied.StatusCode);

        using var approver = CreateClient();
        await LoginAsync(approver, "sam");
        using var approvedResponse = await approver.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(submitted.Version, "approved", null), TestContext.Current.CancellationToken);
        approvedResponse.EnsureSuccessStatusCode();
        var approved = await approvedResponse.Content.ReadFromJsonAsync<Requisition>(TestContext.Current.CancellationToken);
        Assert.NotNull(approved);
        Assert.Equal("approved", approved.Status);
        Assert.Equal(submitted.Version + 1, approved.Version);
        var audit = await approver.GetFromJsonAsync<AuditEntry[]>($"/api/requisitions/{draft.Id}/audit", TestContext.Current.CancellationToken);
        Assert.NotNull(audit);
        Assert.Equal(["draft", "submitted", "approved"], audit.Select(x => x.ToStatus));
        Assert.Equal([1, 3, 4], audit.Select(x => x.Version));
        Assert.Equal(draft.RequesterId, audit[0].ActorId);
        Assert.Equal(draft.ApproverId, audit[2].ActorId);
        using var terminal = await approver.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(approved.Version, "rejected", "Synthetic late decision"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, terminal.StatusCode);
    }

    [Fact]
    public async Task RejectionRequiresReasonAndOnlySubmittedRequestsCanBeDecided()
    {
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        var draft = await CreateRequisitionAsync(requester);
        using var approver = CreateClient();
        await LoginAsync(approver, "sam");
        using var early = await approver.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(draft.Version, "approved", null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        using var submitted = await requester.PostAsJsonAsync($"/api/requisitions/{draft.Id}/submit", new VersionInput(1), TestContext.Current.CancellationToken);
        submitted.EnsureSuccessStatusCode();
        using var missing = await approver.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(2, "rejected", null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var oversized = await approver.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(2, "rejected", new string('x', 1001)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        using var rejected = await approver.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(2, "rejected", "  Synthetic scope needs revision  "), TestContext.Current.CancellationToken);
        rejected.EnsureSuccessStatusCode();
        var audit = await requester.GetFromJsonAsync<AuditEntry[]>($"/api/requisitions/{draft.Id}/audit", TestContext.Current.CancellationToken);
        Assert.NotNull(audit);
        Assert.Equal("Synthetic scope needs revision", audit[^1].Reason);
        Assert.Equal("rejected", audit[^1].ToStatus);
    }

    [Fact]
    public async Task NonparticipantCannotReadOrMutateAndApproverCannotCreate()
    {
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        var draft = await CreateRequisitionAsync(requester);
        using var stranger = CreateClient();
        SetSyntheticPrincipal(stranger, Guid.NewGuid(), "approver");
        await AntiforgeryAsync(stranger);
        using var hidden = await stranger.GetAsync($"/api/requisitions/{draft.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var hiddenAudit = await stranger.GetAsync($"/api/requisitions/{draft.Id}/audit", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, hiddenAudit.StatusCode);
        using var denied = await stranger.PostAsJsonAsync($"/api/requisitions/{draft.Id}/decision",
            new DecisionInput(1, "approved", null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var empty = await stranger.GetFromJsonAsync<PageResult<Requisition>>("/api/requisitions", TestContext.Current.CancellationToken);
        Assert.NotNull(empty);
        Assert.Empty(empty.Items);
        using var approver = CreateClient();
        await LoginAsync(approver, "sam");
        using var forbidden = await approver.PostAsJsonAsync("/api/requisitions",
            new RequisitionCreate(Guid.NewGuid(), "Synthetic role", "Synthetic reason"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task ConcurrentEditsHaveOneWinnerAndCreateReplaysDoNotDuplicateAudit()
    {
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        var create = new RequisitionCreate(Guid.NewGuid(), "Synthetic idempotent role", "Synthetic reason");
        using var firstResponse = await requester.PostAsJsonAsync("/api/requisitions", create, TestContext.Current.CancellationToken);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<Requisition>(TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        using var replayResponse = await requester.PostAsJsonAsync("/api/requisitions", create, TestContext.Current.CancellationToken);
        replayResponse.EnsureSuccessStatusCode();
        var replay = await replayResponse.Content.ReadFromJsonAsync<Requisition>(TestContext.Current.CancellationToken);
        Assert.Equal(first.Id, replay?.Id);
        using var mismatch = await requester.PostAsJsonAsync("/api/requisitions", create with { Title = "Synthetic different input" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        var results = await Task.WhenAll(
            requester.PutAsJsonAsync($"/api/requisitions/{first.Id}", new RequisitionUpdate("Synthetic edit A", "Synthetic reason", 1), TestContext.Current.CancellationToken),
            requester.PutAsJsonAsync($"/api/requisitions/{first.Id}", new RequisitionUpdate("Synthetic edit B", "Synthetic reason", 1), TestContext.Current.CancellationToken));
        try
        {
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in results) { response.Dispose(); }
        }

        using var afterEditReplay = await requester.PostAsJsonAsync("/api/requisitions", create, TestContext.Current.CancellationToken);
        afterEditReplay.EnsureSuccessStatusCode();
        var audit = await requester.GetFromJsonAsync<AuditEntry[]>($"/api/requisitions/{first.Id}/audit", TestContext.Current.CancellationToken);
        Assert.NotNull(audit);
        Assert.Single(audit);
        Assert.Equal(1, audit[0].Version);
    }

    [Fact]
    public async Task ValidationBoundsAndNotesPrivacyAreEnforcedAndAuditCannotBeRewritten()
    {
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        using var malformed = await requester.PostAsJsonAsync("/api/requisitions", new
        {
            requestId = Guid.NewGuid(),
            title = "Synthetic role",
            reason = "Synthetic reason",
            approverId = Guid.NewGuid(),
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var tooLong = await requester.PostAsJsonAsync("/api/requisitions",
            new RequisitionCreate(Guid.NewGuid(), new string('x', 121), "Synthetic reason"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using var unbounded = await requester.GetAsync("/api/requisitions?limit=51", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unbounded.StatusCode);
        var draft = await CreateRequisitionAsync(requester);
        var page = await requester.GetFromJsonAsync<PageResult<Requisition>>("/api/requisitions?limit=1", TestContext.Current.CancellationToken);
        Assert.NotNull(page);
        Assert.Single(page.Items);
        var note = new NoteCreate(Guid.NewGuid(), $"Synthetic private note {Guid.NewGuid()}");
        using var noteCreated = await requester.PostAsJsonAsync("/api/notes", note, TestContext.Current.CancellationToken);
        noteCreated.EnsureSuccessStatusCode();
        using var approver = CreateClient();
        await LoginAsync(approver, "sam");
        var otherNotes = await approver.GetFromJsonAsync<PageResult<Note>>("/api/notes", TestContext.Current.CancellationToken);
        Assert.NotNull(otherNotes);
        Assert.DoesNotContain(otherNotes.Items, x => x.Body == note.Body);
        await using var sql = factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
            "UPDATE monolith.requisition_audit SET to_status = 'approved' WHERE requisition_id = $1");
        sql.Parameters.AddWithValue(draft.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await sql.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task AuditConstraintFailureRollsBackTheDecision()
    {
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        var draft = await CreateRequisitionAsync(requester);
        using var submitted = await requester.PostAsJsonAsync($"/api/requisitions/{draft.Id}/submit", new VersionInput(1), TestContext.Current.CancellationToken);
        submitted.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var requisitions = scope.ServiceProvider.GetRequiredService<Requisitions>();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await requisitions.TransitionAsync(draft.ApproverId, draft.Id, true, "rejected", new string('x', 1001), 2, TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        var current = await requisitions.GetAsync(draft.RequesterId, draft.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        Assert.Equal("submitted", current.Status);
        Assert.Equal(2, current.Version);
        var audit = await requisitions.AuditAsync(draft.RequesterId, draft.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, audit.Count);
        Assert.Equal("submitted", audit[^1].ToStatus);
    }

    [Fact]
    public async Task RenderedRazorFormsCreateEditSubmitAndApproveWithoutUnrelatedFields()
    {
        var validation = new ValidationKeysRecorder();
        await using var observed = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(validation))));
        using var requester = observed.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        await LoginAsync(requester, "alex");
        requester.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var create = ReadRenderedForm(await requester.GetStringAsync("/Requisitions", TestContext.Current.CancellationToken), "Create");
        Assert.True(Guid.TryParse(create.Fields["RequestId"], out _));
        create.Fields["Title"] = "Synthetic reliability engineer";
        create.Fields["Reason"] = "Synthetic replacement request with explicit context for its designated approver.";
        using var createContent = new FormUrlEncodedContent(create.Fields);
        using var created = await requester.PostAsync(create.Action, createContent, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine("Invalid ModelState field names: " + string.Join(", ", validation.Keys));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Empty(validation.Keys);
        Assert.NotNull(created.Headers.Location);
        var selected = created.Headers.Location.OriginalString;
        Assert.NotNull(requester.BaseAddress);
        var id = Guid.Parse(QueryHelpers.ParseQuery(new Uri(requester.BaseAddress, selected).Query)["id"].ToString());
        var edit = ReadRenderedForm(await requester.GetStringAsync(selected, TestContext.Current.CancellationToken), "Update");
        Assert.Equal(id.ToString(), edit.Fields["Id"]);
        edit.Fields["Title"] = "Synthetic reliability engineer, revised";
        edit.Fields["Reason"] = create.Fields["Reason"];
        using var editContent = new FormUrlEncodedContent(edit.Fields);
        using var edited = await requester.PostAsync(edit.Action, editContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        var submit = ReadRenderedForm(await requester.GetStringAsync(selected, TestContext.Current.CancellationToken), "Submit");
        Assert.Equal("2", submit.Fields["Version"]);
        using var submitContent = new FormUrlEncodedContent(submit.Fields);
        using var submitted = await requester.PostAsync(submit.Action, submitContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);

        using var approver = observed.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        await LoginAsync(approver, "sam");
        approver.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var decision = ReadRenderedForm(await approver.GetStringAsync(selected, TestContext.Current.CancellationToken), "Decide");
        Assert.Equal("3", decision.Fields["Version"]);
        decision.Fields["Decision"] = "approved";
        decision.Fields["DecisionReason"] = "";
        using var decisionContent = new FormUrlEncodedContent(decision.Fields);
        using var decided = await approver.PostAsync(decision.Action, decisionContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, decided.StatusCode);
        Assert.Empty(validation.Keys);
        var terminal = await approver.GetStringAsync(selected, TestContext.Current.CancellationToken);
        Assert.Contains("Decision recorded", terminal);
        var final = await requester.GetFromJsonAsync<Requisition>($"/api/requisitions/{id}", TestContext.Current.CancellationToken);
        Assert.NotNull(final);
        Assert.Equal("approved", final.Status);
        Assert.Equal(4, final.Version);
        var audit = await requester.GetFromJsonAsync<AuditEntry[]>($"/api/requisitions/{id}/audit", TestContext.Current.CancellationToken);
        Assert.NotNull(audit);
        Assert.Equal(["draft", "submitted", "approved"], audit.Select(x => x.ToStatus));
    }

    [Fact]
    public async Task NativeSubmitRejectsUnsavedEditsAndRetainsTheEnteredText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var requester = CreateClient();
        await LoginAsync(requester, "alex");
        var draft = await CreateRequisitionAsync(requester);
        requester.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var selected = $"/Requisitions?id={draft.Id}";
        var html = await requester.GetStringAsync(selected, cancellationToken);
        Assert.Contains("form=\"draft-edit\"", html);
        var submit = ReadRenderedForm(html, "Submit");
        Assert.Equal(draft.Title, submit.Fields["Title"]);
        Assert.Equal(draft.Reason, submit.Fields["Reason"]);
        submit.Fields["Title"] = "Synthetic unsaved title";
        submit.Fields["Reason"] = "Synthetic unsaved reason that must remain available after submission is refused.";
        using var content = new FormUrlEncodedContent(submit.Fields);
        using var rejected = await requester.PostAsync(submit.Action, content, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var preservedHtml = await rejected.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("Save changes before submitting", preservedHtml);
        var preserved = ReadRenderedForm(preservedHtml, "Update");
        Assert.Equal(submit.Fields["Title"], preserved.Fields["Title"]);
        Assert.Equal(submit.Fields["Reason"], preserved.Fields["Reason"]);
        var saved = await requester.GetFromJsonAsync<Requisition>($"/api/requisitions/{draft.Id}", cancellationToken);
        Assert.NotNull(saved);
        Assert.Equal(draft.Title, saved.Title);
        Assert.Equal(draft.Reason, saved.Reason);
        Assert.Equal("draft", saved.Status);
        Assert.Equal(draft.Version, saved.Version);
        var audit = await requester.GetFromJsonAsync<AuditEntry[]>($"/api/requisitions/{draft.Id}/audit", cancellationToken);
        Assert.NotNull(audit);
        Assert.Single(audit);
    }

    private static (string Action, Dictionary<string, string> Fields) ReadRenderedForm(string html, string handler)
    {
        // These are the application's rendered forms; this deliberately is not a general HTML parser.
        var forms = Regex.Matches(html, "<form\\b(?<attributes>[^>]*)>(?<body>.*?)</form>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var button = Regex.Matches(html, "<button\\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            .Cast<Match>().FirstOrDefault(match => ReadAttribute(match.Value, "formaction")?.Contains("handler=" + handler, StringComparison.Ordinal) == true);
        foreach (Match form in forms)
        {
            var action = ReadAttribute(form.Groups["attributes"].Value, "action");
            if (button is not null && ReadAttribute(form.Groups["attributes"].Value, "id") == ReadAttribute(button.Value, "form"))
            {
                action = ReadAttribute(button.Value, "formaction");
            }
            else if (action is null || !action.Contains("handler=" + handler, StringComparison.Ordinal)) { continue; }
            Assert.NotNull(action);
            Assert.StartsWith("/", action, StringComparison.Ordinal);
            Assert.False(action.StartsWith("//", StringComparison.Ordinal));
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match input in Regex.Matches(form.Groups["body"].Value, "<input\\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                if (ReadAttribute(input.Value, "type") is "radio" or "checkbox" or "submit") { continue; }
                var name = ReadAttribute(input.Value, "name");
                if (name is not null) { fields[name] = ReadAttribute(input.Value, "value") ?? ""; }
            }

            foreach (Match textarea in Regex.Matches(form.Groups["body"].Value, "<textarea\\b(?<attributes>[^>]*)>(?<body>.*?)</textarea>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                var name = ReadAttribute(textarea.Groups["attributes"].Value, "name");
                if (name is not null)
                {
                    var text = textarea.Groups["body"].Value;
                    if (text.StartsWith("\r\n", StringComparison.Ordinal)) { text = text[2..]; }
                    else if (text.StartsWith('\n')) { text = text[1..]; }
                    fields[name] = WebUtility.HtmlDecode(text);
                }
            }

            Assert.Contains("__RequestVerificationToken", fields.Keys);
            return (action, fields);
        }

        throw new InvalidOperationException("Expected rendered form handler: " + handler);
    }

    private static string? ReadAttribute(string html, string name)
    {
        var match = Regex.Match(html, "\\b" + Regex.Escape(name) + "=\"([^\"]*)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private sealed class ValidationKeysRecorder : IAsyncPageFilter
    {
        public string[] Keys { get; private set; } = [];
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            Keys = context.ModelState.Where(x => x.Value?.Errors.Count > 0).Select(x => x.Key).ToArray();
            await next();
        }
    }

    [Fact]
    public async Task LoginChallengeStaysRelativeDespiteHostAndForwardedHeaders()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Requisitions");
        request.Headers.Host = "untrusted.example";
        request.Headers.Add("X-Forwarded-Host", "another-untrusted.example");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.False(response.Headers.Location.IsAbsoluteUri);
        Assert.Equal("/Login", response.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task ProductionDoesNotExposeInspection()
    {
        using var client = CreateClient();
        foreach (var path in new[] { "/reference/", "/reference/scalar.js", "/openapi/v1.json" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task LocalInspectionRequiresAuthenticationAndUsesPackagedAssetsAndRealSchemas()
    {
        await using var local = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Local"));
        using var client = local.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        foreach (var path in new[] { "/reference/", "/reference/scalar.js", "/openapi/v1.json" })
        {
            using var anonymous = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.Contains("/Login", anonymous.Headers.Location?.ToString());
        }

        await LoginAsync(client, "alex");
        using var reference = await client.GetAsync("/reference/", TestContext.Current.CancellationToken);
        reference.EnsureSuccessStatusCode();
        var html = await reference.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("scalar.js", html);
        Assert.DoesNotContain("cdn.jsdelivr.net", html);
        Assert.DoesNotContain("fonts.googleapis.com", html);
        Assert.True(reference.Headers.TryGetValues("Content-Security-Policy", out var policies));
        Assert.Contains("'nonce-", string.Join(" ", policies));
        var scripts = Regex.Matches(html, "<script[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        Assert.NotEmpty(scripts);
        Assert.All(scripts.Cast<Match>(), script => Assert.Contains("nonce=", script.Value));
        using var asset = await client.GetAsync("/reference/scalar.js", TestContext.Current.CancellationToken);
        asset.EnsureSuccessStatusCode();
        Assert.Contains("javascript", asset.Content.Headers.ContentType?.MediaType);
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/", document.RootElement.GetProperty("servers")[0].GetProperty("url").GetString());
        var create = document.RootElement.GetProperty("paths").GetProperty("/api/requisitions").GetProperty("post");
        Assert.True(create.TryGetProperty("requestBody", out _));
        Assert.Contains(create.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.True(document.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("RequisitionCreate", out _));
    }

    private void SetSyntheticPrincipal(HttpClient client, Guid owner, string role)
    {
        // Only the test harness signs this nonparticipant ticket; no test-login route exists.
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(LabAuthentication.Scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, owner.ToString()),
            new Claim(ClaimTypes.Name, "synthetic-nonparticipant"),
            new Claim(ClaimTypes.Role, role),
        ], LabAuthentication.Scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        }, LabAuthentication.Scheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    private static async Task<Requisition> CreateRequisitionAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/requisitions",
            new RequisitionCreate(Guid.NewGuid(), "Synthetic platform role", "Synthetic replacement request"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<Requisition>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        return created;
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static string Password(string name)
    {
        var key = $"Auth__{char.ToUpperInvariant(name[0])}{name[1..]}PasswordFile";
        return File.ReadAllText(Environment.GetEnvironmentVariable(key) ?? $"/run/secrets/{name}_password").TrimEnd('\r', '\n');
    }

    private static async Task LoginAsync(HttpClient client, string name)
    {
        await AntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/api/session", new LoginInput(name, Password(name)), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await AntiforgeryAsync(client);
    }

    private static async Task AntiforgeryAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/antiforgery", TestContext.Current.CancellationToken);
        Assert.NotNull(csrf?.Token);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(keyPath))
        {
            Directory.Delete(keyPath, recursive: true);
        }
    }

    private sealed record Csrf(string Token);
    private sealed record Session(string Name);
}
