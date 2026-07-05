/**
 * RBAC Middleware
 * 
 * Enforces role-based access control at the application boundary.
 */

import { Request, Response, NextFunction } from 'express';
import { Role, Permission, hasPermission, hasAnyPermission } from '../../../domain/auth/rbac';
import { AppError } from '../../../shared/errors/app-error';

/**
 * Extend Express Request to include user with role
 */
declare global {
    namespace Express {
        interface Request {
            user?: {
                id: string;
                role: Role;
                [key: string]: any;
            };
        }
    }
}

/**
 * Require specific role
 */
export function requireRole(role: Role) {
    return (req: Request, res: Response, next: NextFunction): void => {
        const user = req.user;

        if (!user) {
            throw AppError.unauthorized('Authentication required');
        }

        if (user.role !== role) {
            throw AppError.forbidden(`Requires ${role} role`);
        }

        next();
    };
}

/**
 * Require any of the specified roles
 */
export function requireAnyRole(roles: Role[]) {
    return (req: Request, res: Response, next: NextFunction): void => {
        const user = req.user;

        if (!user) {
            throw AppError.unauthorized('Authentication required');
        }

        if (!roles.includes(user.role)) {
            throw AppError.forbidden(`Requires one of: ${roles.join(', ')}`);
        }

        next();
    };
}

/**
 * Require specific permission
 */
export function requirePermission(permission: Permission) {
    return (req: Request, res: Response, next: NextFunction): void => {
        const user = req.user;

        if (!user) {
            throw AppError.unauthorized('Authentication required');
        }

        if (!hasPermission(user.role, permission)) {
            throw AppError.forbidden('Insufficient permissions');
        }

        next();
    };
}

/**
 * Require any of the specified permissions
 */
export function requireAnyPermission(permissions: Permission[]) {
    return (req: Request, res: Response, next: NextFunction): void => {
        const user = req.user;

        if (!user) {
            throw AppError.unauthorized('Authentication required');
        }

        if (!hasAnyPermission(user.role, permissions)) {
            throw AppError.forbidden('Insufficient permissions');
        }

        next();
    };
}

/**
 * Require admin role (convenience function)
 */
export const requireAdmin = requireRole(Role.ADMIN);

/**
 * Require authenticated user (any role except guest)
 */
export function requireAuth(req: Request, res: Response, next: NextFunction): void {
    const user = req.user;

    if (!user) {
        throw AppError.unauthorized('Authentication required');
    }

    if (user.role === Role.GUEST) {
        throw AppError.forbidden('Authenticated user required');
    }

    next();
}
