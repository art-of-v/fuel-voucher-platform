/**
 * Drizzle User Repository
 * 
 * Concrete implementation of IUserRepository using Drizzle ORM.
 */

import { db } from '../../../../shared/database/db';
import { eq } from 'drizzle-orm';
import { users, type User as DbUser } from '../../../../shared/database/schema';
import {
    IUserRepository,
    User,
    CreateUserData,
    UpdateUserData
} from '../../../../domain/repositories/user.repository';

/**
 * Map database user to domain user
 */
function mapToDomain(dbUser: DbUser): User {
    return {
        id: dbUser.id,
        email: dbUser.email,
        phone: dbUser.phone,
        firstName: dbUser.firstName,
        lastName: dbUser.lastName,
        birthdate: dbUser.birthdate,
        profileImageUrl: dbUser.profileImageUrl,
        vehicleMake: dbUser.vehicleMake,
        vehicleModel: dbUser.vehicleModel,
        vehiclePlate: dbUser.vehiclePlate,
        vehicleFuelType: dbUser.vehicleFuelType,
        referralCode: dbUser.referralCode,
        referredBy: dbUser.referredBy,
        bonusBalance: dbUser.bonusBalance ?? 0,
        createdAt: dbUser.createdAt ?? new Date(),
        updatedAt: dbUser.updatedAt ?? new Date(),
    };
}

export class DrizzleUserRepository implements IUserRepository {
    constructor(private readonly _db: typeof db | any = db) { }

    async findById(id: string): Promise<User | null> {
        const [user] = await this._db.select().from(users).where(eq(users.id, id));
        return user ? mapToDomain(user) : null;
    }

    async findByEmail(email: string): Promise<User | null> {
        const [user] = await this._db.select().from(users).where(eq(users.email, email));
        return user ? mapToDomain(user) : null;
    }

    async findByPhone(phone: string): Promise<User | null> {
        const [user] = await this._db.select().from(users).where(eq(users.phone, phone));
        return user ? mapToDomain(user) : null;
    }

    async findByReferralCode(code: string): Promise<User | null> {
        const [user] = await this._db.select().from(users).where(eq(users.referralCode, code));
        return user ? mapToDomain(user) : null;
    }

    async findAll(): Promise<User[]> {
        const allUsers = await this._db.select().from(users);
        return allUsers.map(mapToDomain);
    }

    async create(data: CreateUserData): Promise<User> {
        const [user] = await this._db.insert(users).values(data).returning();
        return mapToDomain(user);
    }

    async createWithPhone(phone: string): Promise<User> {
        const [user] = await this._db.insert(users).values({ phone }).returning();
        return mapToDomain(user);
    }

    async update(id: string, data: UpdateUserData): Promise<User> {
        const [user] = await this._db
            .update(users)
            .set({ ...data, updatedAt: new Date() })
            .where(eq(users.id, id))
            .returning();
        return mapToDomain(user);
    }

    async upsert(data: CreateUserData & { id?: string }): Promise<User> {
        const [user] = await this._db
            .insert(users)
            .values(data)
            .onConflictDoUpdate({
                target: users.id,
                set: {
                    ...data,
                    updatedAt: new Date(),
                },
            })
            .returning();
        return mapToDomain(user);
    }

    async save(entity: User): Promise<User> {
        // Perform a direct upsert using the database schema's types
        const values = {
            id: entity.id,
            email: entity.email,
            phone: entity.phone,
            firstName: entity.firstName,
            lastName: entity.lastName,
            birthdate: entity.birthdate,
            profileImageUrl: entity.profileImageUrl,
            vehicleMake: entity.vehicleMake,
            vehicleModel: entity.vehicleModel,
            vehiclePlate: entity.vehiclePlate,
            vehicleFuelType: entity.vehicleFuelType,
            referralCode: entity.referralCode,
            referredBy: entity.referredBy,
            bonusBalance: entity.bonusBalance,
            updatedAt: new Date(),
        };

        const [user] = await this._db
            .insert(users)
            .values(values)
            .onConflictDoUpdate({
                target: users.id,
                set: values,
            })
            .returning();
        return mapToDomain(user);
    }

    async delete(id: string): Promise<void> {
        await this._db.delete(users).where(eq(users.id, id));
    }

    async count(): Promise<number> {
        const result = await this._db.select().from(users);
        return result.length;
    }

    async exists(id: string): Promise<boolean> {
        const [user] = await this._db.select({ id: users.id }).from(users).where(eq(users.id, id));
        return !!user;
    }
}

// Export singleton instance
export const drizzleUserRepository = new DrizzleUserRepository();
