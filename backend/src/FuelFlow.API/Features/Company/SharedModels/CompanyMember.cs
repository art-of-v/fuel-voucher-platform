using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Company.SharedModels;

public sealed class CompanyMember
{
    public Guid Id { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid WorkerUserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }

    /// <summary>
    /// Firing a worker sets this rather than removing the row. Employment history is history: the
    /// owner policy (#217) keeps anything that affects it forever, and "this person worked here
    /// until March" is a fact about the company, not garbage a cleanup job may collect. The
    /// <c>!IsDeleted</c> query filter keeps it out of every membership read.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the membership was soft-deleted, or null while it stands.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    public LegalEntity LegalEntity { get; set; } = null!;
    public User WorkerUser { get; set; } = null!;
}
