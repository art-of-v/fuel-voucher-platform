import { db } from "../../shared/database/db";
import { eq } from "drizzle-orm";
import { users, type User, type UpsertUser } from "../../shared/database/schema";

export const usersRepository = {
    async getUser(id: string): Promise<User | undefined> {
        const [user] = await db.select().from(users).where(eq(users.id, id));
        return user;
    },

    async getUserByEmail(email: string): Promise<User | undefined> {
        const [user] = await db.select().from(users).where(eq(users.email, email));
        return user;
    },

    async createUser(userData: UpsertUser): Promise<User> {
        const [user] = await db.insert(users).values(userData).returning();
        return user;
    },

    async upsertUser(userData: UpsertUser): Promise<User> {
        const [user] = await db
            .insert(users)
            .values(userData)
            .onConflictDoUpdate({
                target: users.id,
                set: {
                    ...userData,
                    updatedAt: new Date(),
                },
            })
            .returning();
        return user;
    },

    async getUserByPhone(phone: string): Promise<User | undefined> {
        const [user] = await db.select().from(users).where(eq(users.phone, phone));
        return user;
    },

    async createUserWithPhone(phone: string): Promise<User> {
        const [user] = await db
            .insert(users)
            .values({ phone })
            .returning();
        return user;
    },

    async getAllUsers(): Promise<User[]> {
        return await db.select().from(users);
    },

    async updateUser(id: string, data: Partial<User>): Promise<User> {
        const [updated] = await db
            .update(users)
            .set({ ...data, updatedAt: new Date() })
            .where(eq(users.id, id))
            .returning();
        return updated;
    },

    async getUserByReferralCode(code: string): Promise<User | undefined> {
        const [user] = await db.select().from(users).where(eq(users.referralCode, code));
        return user;
    }
};
