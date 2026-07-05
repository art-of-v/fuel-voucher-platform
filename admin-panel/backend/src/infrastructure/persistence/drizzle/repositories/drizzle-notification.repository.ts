/**
 * Drizzle Notification Repository Implementation
 */

import { eq, and } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { INotificationRepository } from '../../../../domain/repositories/notification.repository';
import { Notification, InsertNotification, notifications } from '../../../../shared/database/schema';

export class DrizzleNotificationRepository implements INotificationRepository {
    async findByUserId(userId: string): Promise<Notification[]> {
        return db
            .select()
            .from(notifications)
            .where(eq(notifications.userId, userId));
    }

    async findUnreadByUserId(userId: string): Promise<Notification[]> {
        return db
            .select()
            .from(notifications)
            .where(and(
                eq(notifications.userId, userId),
                eq(notifications.read, 0)
            ));
    }

    async findById(id: number): Promise<Notification | null> {
        const results = await db
            .select()
            .from(notifications)
            .where(eq(notifications.id, id))
            .limit(1);
        return results[0] || null;
    }

    async create(data: InsertNotification): Promise<Notification> {
        const results = await db
            .insert(notifications)
            .values(data)
            .returning();
        return results[0];
    }

    async markAsRead(id: number): Promise<Notification | null> {
        const results = await db
            .update(notifications)
            .set({ read: 1 })
            .where(eq(notifications.id, id))
            .returning();
        return results[0] || null;
    }

    async markAllAsRead(userId: string): Promise<number> {
        const results = await db
            .update(notifications)
            .set({ read: 1 })
            .where(eq(notifications.userId, userId))
            .returning();
        return results.length;
    }

    async delete(id: number): Promise<boolean> {
        const results = await db
            .delete(notifications)
            .where(eq(notifications.id, id))
            .returning();
        return results.length > 0;
    }
}
