import type { CodedSentence } from "@/lib/coded-error-message";

/**
 * The sentences for a refused contract publish, in the panel's language.
 *
 * The codes come from `AdminContractVersionsController`; a code this map has not learnt keeps the
 * server's English sentence, so a backend-only change still shows the operator something true.
 */
export const PUBLISH_REFUSALS: Record<string, CodedSentence> = {
  contract_brand_required: (_error, t) => t("contracts.error.brandRequired"),
  contract_text_required: (_error, t) => t("contracts.error.textRequired"),
  contract_term_required: (_error, t) => t("contracts.error.termRequired"),
  contract_provider_not_found: (_error, t) =>
    t("contracts.error.providerNotFound"),
  contract_document_required: (_error, t) =>
    t("contracts.error.documentRequired"),
  contract_document_unsupported: (_error, t) =>
    t("contracts.error.documentUnsupported"),
  contract_document_no_text: (_error, t) => t("contracts.error.documentNoText"),
  contract_document_too_large: (error, t) => {
    const size = error.data?.sizeBytes;
    const limit = error.data?.limitBytes;
    if (size === undefined || limit === undefined) return undefined;
    return t(
      "contracts.error.documentTooLarge",
      megabytes(size),
      megabytes(limit),
    );
  },
};

/** One decimal place: the operator judges a limit in megabytes, not in bytes. */
const megabytes = (bytes: number) => (bytes / 1024 / 1024).toFixed(1);
