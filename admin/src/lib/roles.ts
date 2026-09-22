// Role names as returned by the API. Single source of truth for the admin SPA — mirrors the
// backend SeedRoles so a role name is not re-typed as a literal across components.
export const ROLE_PRODUCT_OWNER = 'ProductOwner';
export const ROLE_ADMIN = 'Admin';
export const ROLE_MANAGER = 'Manager';
export const ROLE_USER = 'User';

// Staff roles allowed into the admin panel.
export const STAFF_ROLES: string[] = [ROLE_PRODUCT_OWNER, ROLE_ADMIN, ROLE_MANAGER];

/**
 * Roles the actor may assign to a target, mirroring the backend RoleHierarchy.CanAssignRole
 * (which is the real enforcement point — this only shapes the dropdown). Returns [] when the
 * actor cannot change this target's role, so the caller renders plain text instead of a control.
 *
 * - A ProductOwner target is never changeable by anyone (singleton, untouchable).
 * - ProductOwner actor: User / Manager / Admin — never another ProductOwner (not assignable; the
 *   PO is pinned via the env bootstrap only).
 * - Admin actor: User / Manager, and only for User/Manager targets (Admins are created by the
 *   ProductOwner only).
 * - Manager / User actor: none.
 */
export function assignableRoles(actorRole: string, targetCurrentRole: string): string[] {
  if (targetCurrentRole === ROLE_PRODUCT_OWNER) return [];
  if (actorRole === ROLE_PRODUCT_OWNER) return [ROLE_USER, ROLE_MANAGER, ROLE_ADMIN];
  if (actorRole === ROLE_ADMIN && (targetCurrentRole === ROLE_USER || targetCurrentRole === ROLE_MANAGER)) {
    return [ROLE_USER, ROLE_MANAGER];
  }
  return [];
}
