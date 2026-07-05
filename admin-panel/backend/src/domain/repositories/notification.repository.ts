/**
 * Notification Repository Interface
 * 
 * Defines contract for notification persistence operations.
 */

import { Notification, InsertNotification } from '../../shared/database/schema';

export interface INotificationRepository {
    /**
     * Find all notifications for a user
     */
    findByUserId(userId: string): Promise<Notification[]>;

    /**
     * Find unread notifications for a user
     */
    findUnreadByUserId(userId: string): Promise<Notification[]>;

    /**
     * Find notification by ID
     */
    findById(id: number): Promise<Notification | null>;

    /**
     * Create new notification
     */
    create(data: InsertNotification): Promise<Notification>;

    /**
     * Mark notification as read
     */
    markAsRead(id: number): Promise<Notification | null>;

    /**
     * Mark all notifications as read for a user
     */
    markAllAsRead(userId: string): Promise<number>;

    /**
     * Delete notification
     */
    delete(id: number): Promise<boolean>;
}
