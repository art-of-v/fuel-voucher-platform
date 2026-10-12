import { legalEntityErrorKey } from './legalEntityError';
import { LegalEntityApiError } from '../api/legalEntityApi';

/**
 * The create-company sheet has always mapped 409 and 400 to real copy. The profile screen's
 * company edit did not, and showed one "Error" toast for every outcome instead — so these
 * assertions are about the edit path reusing this mapper, not about new wording.
 */

describe('legalEntityErrorKey', () => {
  it('names the actual problem instead of "Error"', () => {
    expect(legalEntityErrorKey(new LegalEntityApiError(409, 'EDRPOU already registered'))).toBe(
      'context.error.duplicateEdrpou',
    );
    expect(legalEntityErrorKey(new LegalEntityApiError(400, 'EDRPOU must be 8-10 digits'))).toBe(
      'context.error.badRequest',
    );
  });

  it('falls back to the generic key for anything else', () => {
    expect(legalEntityErrorKey(new LegalEntityApiError(500, 'boom'))).toBe('context.error.generic');
    expect(legalEntityErrorKey(new Error('boom'))).toBe('context.error.generic');
    expect(legalEntityErrorKey(undefined)).toBe('context.error.generic');
  });

  it('lets a caller supply its own fallback, because create and edit are different screens', () => {
    // The default says "Could not create the company", which is false on an edit form.
    expect(legalEntityErrorKey(new Error('boom'), 'context.error.editFailed')).toBe(
      'context.error.editFailed',
    );
    // The override only replaces the fallback — a 409 still names the duplicate.
    expect(legalEntityErrorKey(new LegalEntityApiError(409, 'dup'), 'context.error.editFailed')).toBe(
      'context.error.duplicateEdrpou',
    );
  });
});