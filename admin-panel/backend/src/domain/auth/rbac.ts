/**
 * Role-Based Access Control (RBAC)
 * 
 * Defines roles and permissions for the application.
 */

/**
 * User roles
 */
export enum Role {
    ADMIN = 'admin',
    USER = 'user',
    GUEST = 'guest',
}

/**
 * Permissions
 */
export enum Permission {
    // Voucher permissions
    VOUCHER_CREATE = 'voucher:create',
    VOUCHER_READ = 'voucher:read',
    VOUCHER_UPDATE = 'voucher:update',
    VOUCHER_DELETE = 'voucher:delete',
    VOUCHER_ASSIGN = 'voucher:assign',
    VOUCHER_IMPORT = 'voucher:import',

    // Order permissions
    ORDER_CREATE = 'order:create',
    ORDER_READ = 'order:read',
    ORDER_UPDATE = 'order:update',
    ORDER_DELETE = 'order:delete',
    ORDER_FULFILL = 'order:fulfill',

    // User permissions
    USER_CREATE = 'user:create',
    USER_READ = 'user:read',
    USER_UPDATE = 'user:update',
    USER_DELETE = 'user:delete',

    // Station permissions
    STATION_CREATE = 'station:create',
    STATION_READ = 'station:read',
    STATION_UPDATE = 'station:update',
    STATION_DELETE = 'station:delete',

    // Package permissions
    PACKAGE_CREATE = 'package:create',
    PACKAGE_READ = 'package:read',
    PACKAGE_UPDATE = 'package:update',
    PACKAGE_DELETE = 'package:delete',

    // System permissions
    SYSTEM_ADMIN = 'system:admin',
}

/**
 * Role-Permission mapping
 */
export const ROLE_PERMISSIONS: Record<Role, Permission[]> = {
    [Role.ADMIN]: [
        // All permissions
        Permission.VOUCHER_CREATE,
        Permission.VOUCHER_READ,
        Permission.VOUCHER_UPDATE,
        Permission.VOUCHER_DELETE,
        Permission.VOUCHER_ASSIGN,
        Permission.VOUCHER_IMPORT,
        Permission.ORDER_CREATE,
        Permission.ORDER_READ,
        Permission.ORDER_UPDATE,
        Permission.ORDER_DELETE,
        Permission.ORDER_FULFILL,
        Permission.USER_CREATE,
        Permission.USER_READ,
        Permission.USER_UPDATE,
        Permission.USER_DELETE,
        Permission.STATION_CREATE,
        Permission.STATION_READ,
        Permission.STATION_UPDATE,
        Permission.STATION_DELETE,
        Permission.PACKAGE_CREATE,
        Permission.PACKAGE_READ,
        Permission.PACKAGE_UPDATE,
        Permission.PACKAGE_DELETE,
        Permission.SYSTEM_ADMIN,
    ],
    [Role.USER]: [
        // User permissions
        Permission.VOUCHER_READ,
        Permission.ORDER_CREATE,
        Permission.ORDER_READ,
        Permission.USER_READ,
        Permission.USER_UPDATE,
        Permission.STATION_READ,
        Permission.PACKAGE_READ,
    ],
    [Role.GUEST]: [
        // Public permissions
        Permission.STATION_READ,
        Permission.PACKAGE_READ,
    ],
};

/**
 * Check if role has permission
 */
export function hasPermission(role: Role, permission: Permission): boolean {
    return ROLE_PERMISSIONS[role]?.includes(permission) ?? false;
}

/**
 * Check if role has any of the permissions
 */
export function hasAnyPermission(role: Role, permissions: Permission[]): boolean {
    return permissions.some(p => hasPermission(role, p));
}

/**
 * Check if role has all permissions
 */
export function hasAllPermissions(role: Role, permissions: Permission[]): boolean {
    return permissions.every(p => hasPermission(role, p));
}
