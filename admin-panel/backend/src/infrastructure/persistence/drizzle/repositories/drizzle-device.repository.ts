import { db } from "../../../../shared/database/db";
import { devices as devicesTable } from "../../../../shared/database/schema";
import { Device, IDeviceRepository, CreateDeviceData } from "../../../../domain/repositories/device.repository";
import { eq } from "drizzle-orm";

export class DrizzleDeviceRepository implements IDeviceRepository {
    async findByDeviceId(deviceId: string): Promise<Device | null> {
        const [result] = await db
            .select()
            .from(devicesTable)
            .where(eq(devicesTable.deviceId, deviceId))
            .limit(1);

        if (!result) return null;

        return {
            ...result,
            status: result.status || 'ACTIVE',
            createdAt: result.createdAt!,
            lastSeen: result.lastSeen!
        };
    }

    async registerDevice(data: CreateDeviceData): Promise<Device> {
        const [result] = await db
            .insert(devicesTable)
            .values({
                userId: data.userId,
                deviceId: data.deviceId,
                publicKey: data.publicKey,
                deviceModel: data.deviceModel,
                osVersion: data.osVersion,
                appVersion: data.appVersion,
                status: 'ACTIVE'
            })
            .onConflictDoUpdate({
                target: devicesTable.deviceId,
                set: {
                    publicKey: data.publicKey,
                    deviceModel: data.deviceModel,
                    osVersion: data.osVersion,
                    appVersion: data.appVersion,
                    status: 'ACTIVE',
                    lastSeen: new Date()
                }
            })
            .returning();

        return {
            ...result,
            status: result.status || 'ACTIVE',
            createdAt: result.createdAt!,
            lastSeen: result.lastSeen!
        };
    }

    async updateLastSeen(deviceId: string): Promise<void> {
        await db
            .update(devicesTable)
            .set({ lastSeen: new Date() })
            .where(eq(devicesTable.deviceId, deviceId));
    }
}
