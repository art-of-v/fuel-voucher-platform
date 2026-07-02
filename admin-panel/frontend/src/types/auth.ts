export interface SendCodeRequest {
    phoneNumber: string;
}

export interface SendCodeResponse {
    success: boolean;
    message?: string;
}

export interface VerifyCodeRequest {
    phoneNumber: string;
    code: string;
}

export interface VerifyCodeResponse {
    success: boolean;
    user?: User;
    refreshToken?: string;
    accessToken?: string;
}

export interface RefreshTokenRequest {
    refreshToken: string;
}

export interface RefreshTokenResponse {
    success: boolean;
    accessToken?: string;
    refreshToken?: string;
}

export interface User {
    id: string;
    phoneNumber: string;
    email?: string;
    firstName?: string;
    lastName?: string;
    profileImageUrl?: string;
    bonusBalance: number;
    vehicleMake?: string;
    vehicleModel?: string;
    vehiclePlate?: string;
    vehicleFuelType?: string;
    referralCode?: string;
    referredBy?: string;
}
