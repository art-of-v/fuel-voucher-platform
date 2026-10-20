using System.Security.Claims;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetImportBatches;

/// <summary>
/// The whole batch, in one question: what do its vouchers look like, and how many are there.
/// </summary>
/// <param name="ImportId">The import whose batch this is.</param>
/// <param name="Page">The page to read, one-based.</param>
/// <param name="PageSize">How many rows a page holds.</param>
public sealed record GetImportBatchVouchersQuery(
    Guid ImportId,
    int Page = 1,
    int PageSize = 50);
