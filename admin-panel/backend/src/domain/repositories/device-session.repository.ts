export interface DeviceSession {
    id: string;
    userId: string;
    deviceId: string;
    refreshToken: string;
    expiresAt: Date;
    createdAt: Date;
}

export interface CreateDeviceSessionData {
    userId: string;
    deviceId: string;
    refreshToken: string;
    expiresAt: Date;
}

export interface IDeviceSessionRepository {
    createSession(data: CreateDeviceSessionData): Promise<DeviceSession>;
    findByRefreshToken(token: string): Promise<DeviceSession | null>;
    revokeSession(token: string): Promise<void>;
    revokeAllForDevice(deviceId: string): Promise<void>;
}
