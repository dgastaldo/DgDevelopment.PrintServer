using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;
using PrintServer.Queue;
using PrintServer.WebUi.Authentication;

namespace PrintServer.WebUi;

public sealed class WebUiActions(
    AuthenticationStateProvider authentication,
    IAuthorizationService authorization,
    IServiceProvider services,
    UiText text)
{
    private string Denied => text["Error.Denied"];

    private string QueueOff => text["Error.QueueOff"];

    public async Task<QueueActionResult> RetryAsync(Guid id, CancellationToken ct)
    {
        if (!await IsAllowedAsync(WebUiPermissions.ManagePolicy).ConfigureAwait(false))
        {
            return QueueActionResult.Failure(Denied);
        }

        var admin = services.GetService<JobAdministrator>();
        return admin is null ? QueueActionResult.Failure(QueueOff) : await admin.RetryAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<QueueActionResult> CancelAsync(Guid id, CancellationToken ct)
    {
        if (!await IsAllowedAsync(WebUiPermissions.ManagePolicy).ConfigureAwait(false))
        {
            return QueueActionResult.Failure(Denied);
        }

        var admin = services.GetService<JobAdministrator>();
        return admin is null ? QueueActionResult.Failure(QueueOff) : await admin.CancelAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<SubmissionResult> AddAsync(string fileName, Stream content, string? printerName, PrintOptions options, CancellationToken ct)
    {
        if (!await IsAllowedAsync(WebUiPermissions.AddPolicy).ConfigureAwait(false))
        {
            return SubmissionResult.Failure(Denied);
        }

        var submitter = services.GetService<IJobSubmitter>();
        return submitter is null
            ? SubmissionResult.Failure(QueueOff)
            : await submitter.SubmitAsync(fileName, content, printerName, options, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsAllowedAsync(string policy)
    {
        var state = await authentication.GetAuthenticationStateAsync().ConfigureAwait(false);
        return (await authorization.AuthorizeAsync(state.User, policy).ConfigureAwait(false)).Succeeded;
    }
}
