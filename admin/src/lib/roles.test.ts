import { describe, it, expect } from "vitest";

import {
  assignableRoles,
  canSetPromoAccess,
  ROLE_ADMIN,
  ROLE_MANAGER,
  ROLE_PRODUCT_OWNER,
  ROLE_QA,
  ROLE_USER,
} from "./roles";

/**
 * The role dropdown mirrors the backend's hierarchy, and the backend is the enforcement point.
 * These cases exist because the two disagreed: an account with no role at all is a reachable
 * state (`users.role_id` is nullable), and the dropdown used to read that state as 'User' — which
 * hid it from the operator and made the repair impossible.
 */
describe("assignableRoles", () => {
  it("gives the ProductOwner the roles for an account that has no role", () => {
    // The only way out of the state: the owner may place an account the hierarchy cannot place.
    expect(assignableRoles(ROLE_PRODUCT_OWNER, null)).toEqual([
      ROLE_USER,
      ROLE_MANAGER,
      ROLE_ADMIN,
    ]);
  });

  it("never offers the retired QA role to assign", () => {
    // The backend refuses to hand it out (SeedRoles.IsValidRoleName), so a dropdown that offers
    // it is a control that is guaranteed to fail.
    expect(assignableRoles(ROLE_PRODUCT_OWNER, ROLE_USER)).not.toContain(
      ROLE_QA,
    );
    expect(assignableRoles(ROLE_PRODUCT_OWNER, null)).not.toContain(ROLE_QA);
  });

  it("gives an Admin nothing for an account that has no role", () => {
    // The backend refuses this one for everyone below the ProductOwner, so a dropdown that is
    // guaranteed to 403 is worse than no dropdown.
    expect(assignableRoles(ROLE_ADMIN, null)).toEqual([]);
  });

  it("gives an Admin User and Manager for a User or Manager target", () => {
    expect(assignableRoles(ROLE_ADMIN, ROLE_USER)).toEqual([
      ROLE_USER,
      ROLE_MANAGER,
    ]);
    expect(assignableRoles(ROLE_ADMIN, ROLE_MANAGER)).toEqual([
      ROLE_USER,
      ROLE_MANAGER,
    ]);
  });

  it("gives an Admin nothing for an Admin target", () => {
    expect(assignableRoles(ROLE_ADMIN, ROLE_ADMIN)).toEqual([]);
  });

  it("never lets anyone change a ProductOwner's role", () => {
    expect(assignableRoles(ROLE_PRODUCT_OWNER, ROLE_PRODUCT_OWNER)).toEqual([]);
    expect(assignableRoles(ROLE_ADMIN, ROLE_PRODUCT_OWNER)).toEqual([]);
  });

  it("never offers ProductOwner as a role to assign", () => {
    // The PO is pinned by the env bootstrap only; the app can never mint one.
    expect(assignableRoles(ROLE_PRODUCT_OWNER, ROLE_USER)).not.toContain(
      ROLE_PRODUCT_OWNER,
    );
  });

  it("gives the employee tier nothing at all", () => {
    expect(assignableRoles(ROLE_MANAGER, ROLE_USER)).toEqual([]);
    expect(assignableRoles(ROLE_QA, ROLE_USER)).toEqual([]);
    expect(assignableRoles(ROLE_USER, ROLE_USER)).toEqual([]);
  });
});

/**
 * Who may move the promotion switch of a customer — deliberately WIDER than the role dropdown.
 * The ruling of 2026-11-06 put the decision in the Manager's hands (the person who signs a
 * customer up to the promotion is the person who talks to them), and a Manager mints no roles.
 * `SeedRoles.PromoAccessRoleNames` behind the `PromoAccess` policy is the enforcement point; this
 * only decides whether the control is drawn, so the two lists must agree.
 */
describe("canSetPromoAccess", () => {
  it("admits the employee tier, which may not assign a single role", () => {
    expect(canSetPromoAccess(ROLE_MANAGER)).toBe(true);
    expect(assignableRoles(ROLE_MANAGER, ROLE_USER)).toEqual([]);
  });

  it("admits the staff above it", () => {
    expect(canSetPromoAccess(ROLE_ADMIN)).toBe(true);
    expect(canSetPromoAccess(ROLE_PRODUCT_OWNER)).toBe(true);
  });

  it("refuses the customer, the retired QA role and an actor with no role", () => {
    // A customer deciding which customers are promo customers is exactly what the switch closes.
    expect(canSetPromoAccess(ROLE_USER)).toBe(false);
    expect(canSetPromoAccess(ROLE_QA)).toBe(false);
    expect(canSetPromoAccess("")).toBe(false);
  });
});
