import { LegalEntityApiError } from '../api/legalEntityApi';

/**
 * Maps a legal-entity failure to an i18n key — 409 is a duplicate EDRPOU, 400 a rejected
 * format.
 *
 * Lives in lib/ rather than in the hook so it can be unit-tested without pulling in
 * react-query, the auth store and app state, and so a caller that is not a hook can use it:
 * the profile screen's company edit sits outside features/company entirely and had no way to
 * reuse the mapper the create sheet used.
 */
export function legalEntityErrorKey(error: unknown, genericKey = 'context.error.generic'): string {
  if (error instanceof LegalEntityApiError) {
    if (error.status === 409) return 'context.error.duplicateEdrpou';
    if (error.status === 400) return 'context.error.badRequest';
  }
  return genericKey;
}
