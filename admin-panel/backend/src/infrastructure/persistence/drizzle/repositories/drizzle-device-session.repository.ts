import { db } from "../../../../shared/database/db";
import { deviceSessions as sessionsTable } from "../../../../shared/database/schema";
import { DeviceSession, IDeviceSessionRepository, CreateDeviceSessionData } from "../../../../domain/repositories/device-session.repository";
import { eq, and } from "drizzle-orm";

export class DrizzleDeviceSessionRepository implements IDeviceSessionRepository {
    async createSession(data: CreateDeviceSessionData): Promise<DeviceSession> {
        const [result] = await db
            .insert(sessionsTable)
            .values({
                userId: data.userId,
                deviceId: data.deviceId,
                refreshToken: data.refreshToken,
                expiresAt: data.expiresAt
            })
            .returning();

        return {
            ...result,
            createdAt: result.createdAt!
        };
    }

    async findByRefreshToken(token: string): Promise<DeviceSession | null> {
        const [result] = await db
            .select()
            .from(sessionsTable)
            .where(eq(sessionsTable.refreshToken, token))
            .limit(1);

        if (!result) return null;

        return {
            ...result,
            createdAt: result.createdAt!
        };
    }

    async revokeSession(token: string): Promise<void> {
        await db
            .delete(sessionsTable)
            .where(eq(sessionsTable.refreshToken, token));
    }

    async revokeAllForDevice(deviceId: string): Promise<void> {
        await db
            .delete(sessionsTable)
            .where(eq(sessionsTable.deviceId, deviceId));
    }
}
