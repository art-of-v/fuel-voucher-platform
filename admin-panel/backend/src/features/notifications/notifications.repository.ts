import { db } from "../../shared/database/db";
import { eq, desc } from "drizzle-orm";
import { notifications, type Notification, type InsertNotification } from "../../shared/database/schema";

export const notificationsRepository = {
    async createNotification(notification: InsertNotification): Promise<Notification> {
        const [created] = await db.insert(notifications).values(notification).returning();
        return created;
    },

    async getUserNotifications(userId: string): Promise<Notification[]> {
        return await db.select().from(notifications).where(eq(notifications.userId, userId)).orderBy(desc(notifications.createdAt));
    },

    async markNotificationRead(id: number): Promise<void> {
        await db.update(notifications).set({ read: 1 }).where(eq(notifications.id, id));
    }
};
