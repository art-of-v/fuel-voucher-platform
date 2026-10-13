namespace FuelFlow.Features.Feedback.SharedModels;

/// <summary>
/// What the triage step concluded about a message. Null on the enum's own row means
/// "not classified yet" - see <see cref="FeedbackMessage.Verdict"/>.
/// </summary>
public enum FeedbackVerdict
{
    /// <summary>Greetings, jokes, sticker spam, ads, empty noise - carries no product information.</summary>
    Junk,

    /// <summary>A complaint, a bug, an idea, a question or thanks that says something about the app.</summary>
    Kept
}

/// <summary>What a kept message is about. Drives the admin filters and the staff-chat label.</summary>
public enum FeedbackCategory
{
    Bug,
    Idea,
    Complaint,
    Ux,
    Question,
    Praise,
    Other
}

/// <summary>
/// How much a kept message should pull at the product roadmap. Complaints and bugs carry it;
/// a thank-you note does not.
/// </summary>
public enum FeedbackSeverity
{
    Low,
    Medium,
    High
}

/// <summary>Language of the message, recorded so a mixed-language inbox can be triaged by speaker.</summary>
public enum FeedbackLanguage
{
    Uk,
    Ru,
    En,
    Other
}

/// <summary>
/// Where a message sits in the team's review workflow. Kept separate from
/// <see cref="FeedbackVerdict"/> on purpose: a message can be worth reading (Kept) long after the
/// conversation is over (Handled), and collapsing the two would make "we already did this" and
/// "this told us nothing" the same state.
/// </summary>
public enum FeedbackStatus
{
    /// <summary>Arrived, not yet looked at.</summary>
    New,

    /// <summary>Someone read it and filed it; no decision yet.</summary>
    Triaged,

    /// <summary>Actioned, or deliberately not actioned.</summary>
    Handled,

    /// <summary>Kept as feedback but closed as not actionable (duplicate, out of scope, spam in
    /// substance without looking like spam).</summary>
    Rejected
}