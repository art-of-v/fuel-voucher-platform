export interface Device {
    id: string;
    userId: string;
    deviceId: string;
    publicKey: string;
    deviceModel: string | null;
    osVersion: string | null;
    appVersion: string | null;
    status: string;
    createdAt: Date;
    lastSeen: Date;
}

export interface CreateDeviceData {
    userId: string;
    deviceId: string;
    publicKey: string;
    deviceModel?: string;
    osVersion?: string;
    appVersion?: string;
}

export interface IDeviceRepository {
    findByDeviceId(deviceId: string): Promise<Device | null>;
    registerDevice(data: CreateDeviceData): Promise<Device>;
    updateLastSeen(deviceId: string): Promise<void>;
    updateStatus(deviceId: string, status: string): Promise<void>;
}
