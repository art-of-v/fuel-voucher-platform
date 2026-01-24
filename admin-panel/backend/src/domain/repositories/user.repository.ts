/**
 * User Repository Interface
 * 
 * Defines persistence operations for User entities.
 */

import { IBaseRepository } from './base.repository';

/**
 * User entity (domain representation)
 */
export interface User {
    id: string;
    email: string | null;
    phone: string | null;
    firstName: string | null;
    lastName: string | null;
    birthdate: string | null;
    profileImageUrl: string | null;
    vehicleMake: string | null;
    vehicleModel: string | null;
    vehiclePlate: string | null;
    vehicleFuelType: string | null;
    referralCode: string | null;
    referredBy: string | null;
    bonusBalance: number;
    createdAt: Date;
    updatedAt: Date;
}

/**
 * User creation data
 */
export interface CreateUserData {
    phone?: string;
    email?: string;
    firstName?: string;
    lastName?: string;
}

/**
 * User update data
 */
export interface UpdateUserData {
    firstName?: string;
    lastName?: string;
    birthdate?: string;
    profileImageUrl?: string;
    vehicleMake?: string;
    vehicleModel?: string;
    vehiclePlate?: string;
    vehicleFuelType?: string;
    referralCode?: string;
    referredBy?: string;
    bonusBalance?: number;
}

/**
 * User Repository Interface
 */
export interface IUserRepository extends IBaseRepository<User, string> {
    /**
     * Find user by phone number
     */
    findByPhone(phone: string): Promise<User | null>;

    /**
     * Find user by email
     */
    findByEmail(email: string): Promise<User | null>;

    /**
     * Find user by referral code
     */
    findByReferralCode(code: string): Promise<User | null>;

    /**
     * Create user with phone number
     */
    createWithPhone(phone: string): Promise<User>;

    /**
     * Create user with data
     */
    create(data: CreateUserData): Promise<User>;

    /**
     * Upsert user
     */
    upsert(data: CreateUserData & { id?: string }): Promise<User>;

    /**
     * Update user
     */
    update(id: string, data: UpdateUserData): Promise<User>;

    /**
     * Get all users (for admin)
     */
    findAll(): Promise<User[]>;
}
