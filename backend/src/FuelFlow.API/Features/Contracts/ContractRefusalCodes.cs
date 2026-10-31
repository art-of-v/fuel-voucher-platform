namespace FuelFlow.Features.Contracts;

/// <summary>
/// The stable codes a contract publish is refused under, so the admin can say the reason in the
/// operator's language instead of printing the English sentence.
/// </summary>
/// <remarks>
/// One list for both endpoints: the text box and the document upload refuse the same form for the
/// same reasons, and two lists drift.
/// </remarks>
internal static class ContractRefusalCodes
{
    public const string BrandRequired = "contract_brand_required";

    public const string TextRequired = "contract_text_required";

    public const string TermRequired = "contract_term_required";

    public const string ProviderNotFound = "contract_provider_not_found";

    public const string DocumentRequired = "contract_document_required";

    public const string DocumentUnsupported = "contract_document_unsupported";

    public const string DocumentTooLarge = "contract_document_too_large";

    public const string DocumentNoText = "contract_document_no_text";
}
