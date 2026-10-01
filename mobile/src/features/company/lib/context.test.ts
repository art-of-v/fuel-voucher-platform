import { resolveCurrentCompany, isBusinessContext } from './context';
import type { Company } from '../../../core/types/api';

function makeCompany(overrides: Partial<Company> = {}): Company {
  return {
    id: 'c1',
    name: 'ТОВ Приклад',
    edrpou: '12345678',
    ...overrides,
  };
}

const acme = makeCompany({ id: 'c1', name: 'Acme' });
const globex = makeCompany({ id: 'c2', name: 'Globex' });
const companies = [acme, globex];

describe('resolveCurrentCompany', () => {
  it('resolves a null context to personal (no company)', () => {
    expect(resolveCurrentCompany(null, companies)).toBeNull();
  });

  it('resolves a matching id to that company', () => {
    expect(resolveCurrentCompany('c2', companies)).toBe(globex);
  });

  it('resolves a stale/foreign id to personal (falls back to null)', () => {
    expect(resolveCurrentCompany('does-not-exist', companies)).toBeNull();
  });

  it('resolves to personal when the user owns no companies', () => {
    expect(resolveCurrentCompany('c1', [])).toBeNull();
  });
});

describe('isBusinessContext', () => {
  it('is false for the personal root', () => {
    expect(isBusinessContext(null, companies)).toBe(false);
  });

  it('is true when a company context resolves', () => {
    expect(isBusinessContext('c1', companies)).toBe(true);
  });

  it('is false for a stale id that no longer matches an owned company', () => {
    expect(isBusinessContext('does-not-exist', companies)).toBe(false);
  });
});
