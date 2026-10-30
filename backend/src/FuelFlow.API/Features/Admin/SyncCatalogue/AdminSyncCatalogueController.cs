using System.Security.Claims;
using FuelFlow.BackgroundJobs;
using FuelFlow.SharedKernel.Domain;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Admin.SyncCatalogue;

/// <summary>
/// Runs the catalogue checks now, rather than waiting for the week to come round.
/// </summary>
/// <remarks>
/// <para>
/// The price refresh runs every fifteen minutes and moves prices; whether a vendor sells a grade we
/// do not carry is a different question, on a different clock - a weekly one - because creating a
/// fuel is a decision. A weekly job is right for a system nobody watches, and wrong for the week
/// somebody empties a catalogue and asks for it back.
/// </para>
/// <para>
/// So this is the same three jobs the schedule runs, started on demand. It enqueues them rather than
/// calling them: each one fetches a third party's page, and a request that waits on three of those
/// would hold an HTTP connection open for as long as the slowest vendor takes to answer.
/// </para>
/// <para>
/// The catalogue capability, matching the rest of the admin surface. The jobs do what the schedule
/// already does unattended - fetch, and write what the vendor says - so this grants no new power; it
/// only chooses the moment.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/catalogue/sync")]
[Authorize(Policy = AuthPolicies.CatalogueWrite)]
public sealed class AdminSyncCatalogueController : ControllerBase
{
    private readonly IBackgroundJobClient _jobs;

    public AdminSyncCatalogueController(IBackgroundJobClient jobs) => _jobs = jobs;

    /// <summary>
    /// The vendors whose catalogue is checked, and the job that checks it.
    /// </summary>
    /// <remarks>
    /// The catalogue half of each one - the half that creates a grade we do not carry - rather than
    /// the price half, which runs every fifteen minutes anyway and would only repeat work.
    /// </remarks>
    [HttpPost]
    public IActionResult Sync()
    {
        _jobs.Enqueue<OkkoPriceSyncService>(job => job.SyncCatalogueAndPricesAsync(CancellationToken.None));
        _jobs.Enqueue<UpgPriceSyncService>(job => job.SyncCatalogueAndPricesAsync(CancellationToken.None));
        _jobs.Enqueue<MinfinPriceSyncService>(job => job.SyncCatalogueAndPricesAsync(CancellationToken.None));

        return Accepted(
            "The catalogue check is running for OKKO, UPG, KLO and WOG. What it found is in the "
            + "backend log, and what it changed is in the brands screen.");
    }
}
