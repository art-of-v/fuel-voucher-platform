// Role names as returned by the API. Single source of truth for the admin SPA — mirrors the
// backend SeedRoles so a role name is not re-typed as a literal across components.
export const ROLE_PRODUCT_OWNER = 'ProductOwner';
export const ROLE_ADMIN = 'Admin';
export const ROLE_MANAGER = 'Manager';
export const ROLE_USER = 'User';

// A retired staff role. The backend no longer admits it into the admin surface and no longer
// hands it out (SeedRoles.QaName is retired); it is kept here only so a row the database may
// still carry can be named and shown as what it is, rather than as an unknown role.
export const ROLE_QA = 'QA';

// Staff roles allowed into the admin panel. Mirrors the backend's StaffRoleNames.
export const STAFF_ROLES: string[] = [ROLE_PRODUCT_OWNER, ROLE_ADMIN, ROLE_MANAGER];

/**
 * Roles the actor may assign to a target, mirroring the backend RoleHierarchy.CanAssignRole
 * (which is the real enforcement point — this only shapes the dropdown). Returns [] when the
 * actor cannot change this target's role, so the caller renders plain text instead of a control.
 *
 * - A ProductOwner target is never changeable by anyone (singleton, untouchable).
 * - ProductOwner actor: User / Manager / Admin — never another ProductOwner (not assignable;
 *   the PO is pinned via the env bootstrap only), and never the retired QA role, which the
 *   backend refuses to hand out. A target with NO role is included here: only
 *   the ProductOwner may place such an account, and that is the only way out of the state
 *   (planning #299).
 * - Admin actor: User / Manager, and only for User/Manager targets (Admins are created by the
 *   ProductOwner only). A target with no role is NOT theirs: the backend refuses it, and a
 *   dropdown that is guaranteed to 403 is worse than no dropdown.
 * - Manager / User actor: none. A Manager moves money and flips a User account's active switch,
 *   but it may not mint roles, so its dropdown is empty and it gets no role control.
 */
export function assignableRoles(actorRole: string, targetCurrentRole: string | null): string[] {
  if (targetCurrentRole === ROLE_PRODUCT_OWNER) return [];
  if (actorRole === ROLE_PRODUCT_OWNER) return [ROLE_USER, ROLE_MANAGER, ROLE_ADMIN];
  if (actorRole === ROLE_ADMIN && (targetCurrentRole === ROLE_USER || targetCurrentRole === ROLE_MANAGER)) {
    return [ROLE_USER, ROLE_MANAGER];
  }
  return [];
}
