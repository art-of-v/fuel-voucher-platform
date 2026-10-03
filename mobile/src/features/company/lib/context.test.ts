import { resolveContext, isBusinessContext, isOwnerContext, PERSONAL_CONTEXT } from './context';
import type { Company } from '../../../core/types/api';
import type { MyCompanyMembershipDto } from '../types';

function makeCompany(overrides: Partial<Company> = {}): Company {
  return {
    id: 'c1',
    name: 'ТОВ Приклад',
    edrpou: '12345678',
    ...overrides,
  };
}

function makeMembership(
  overrides: Partial<MyCompanyMembershipDto> = {},
): MyCompanyMembershipDto {
  return {
    memberId: 'm1',
    legalEntityId: 'c1',
    name: 'Acme',
    edrpou: '12345678',
    ownerUserId: 'owner-1',
    isOwner: false,
    joinedAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const acme = makeCompany({ id: 'c1', name: 'Acme' });
const globex = makeCompany({ id: 'c2', name: 'Globex' });
const companies = [acme, globex];

describe('resolveContext — personal', () => {
  it('resolves a null context to the personal root', () => {
    expect(resolveContext(null, companies)).toEqual(PERSONAL_CONTEXT);
  });

  it('resolves a stale/foreign id to personal — a dropped context can never trap the user', () => {
    expect(resolveContext('does-not-exist', companies)).toEqual(PERSONAL_CONTEXT);
  });

  it('resolves to personal when the user owns no companies and works nowhere', () => {
    expect(resolveContext('c1', [], [])).toEqual(PERSONAL_CONTEXT);
  });

  it('resolves to personal once the membership is gone (fired worker)', () => {
    expect(resolveContext('c1', [], [])).toEqual(PERSONAL_CONTEXT);
  });
});

describe('resolveContext — owner', () => {
  it('resolves a matching owned id to an owner context with that company', () => {
    const ctx = resolveContext('c2', companies);
    expect(ctx.kind).toBe('owner');
    expect(ctx.company).toBe(globex);
    expect(ctx.membership).toBeNull();
  });

  it('prefers owner rights when the user both owns and works for the same company', () => {
    const membership = makeMembership({ legalEntityId: 'c1', isOwner: true });
    const ctx = resolveContext('c1', companies, [membership]);
    expect(ctx.kind).toBe('owner');
    expect(ctx.company).toBe(acme);
  });
});

describe('resolveContext — worker', () => {
  const membership = makeMembership({ legalEntityId: 'c3', name: 'Initech' });

  it('resolves a company the user only works for, exposing the company for headers', () => {
    const ctx = resolveContext('c3', [], [membership]);
    expect(ctx.kind).toBe('worker');
    expect(ctx.membership).toBe(membership);
    expect(ctx.company).toEqual({ id: 'c3', name: 'Initech', edrpou: '12345678' });
  });

  it('never turns a membership into owner rights', () => {
    expect(isOwnerContext(resolveContext('c3', [], [membership]))).toBe(false);
  });

  it('keeps several worker contexts apart', () => {
    const other = makeMembership({ memberId: 'm2', legalEntityId: 'c4', name: 'Globex' });
    const memberships = [membership, other];
    expect(resolveContext('c3', [], memberships).company?.name).toBe('Initech');
    expect(resolveContext('c4', [], memberships).company?.name).toBe('Globex');
  });

  it('treats a membership as business context but not as an owner context', () => {
    const ctx = resolveContext('c3', [], [membership]);
    expect(isBusinessContext(ctx)).toBe(true);
    expect(isOwnerContext(ctx)).toBe(false);
  });
});

describe('isBusinessContext / isOwnerContext', () => {
  it('is false for the personal root', () => {
    expect(isBusinessContext(resolveContext(null, companies))).toBe(false);
    expect(isOwnerContext(resolveContext(null, companies))).toBe(false);
  });

  it('is true only in an owner context for the owner-only checks', () => {
    expect(isBusinessContext(resolveContext('c1', companies))).toBe(true);
    expect(isOwnerContext(resolveContext('c1', companies))).toBe(true);
  });
});