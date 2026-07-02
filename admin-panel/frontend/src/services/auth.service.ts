import { SendCodeRequest, SendCodeResponse, VerifyCodeRequest, VerifyCodeResponse, RefreshTokenRequest, RefreshTokenResponse, User } from '@/types/auth';
import { apiRequest } from '@/lib/utils';

export const authApi = {
    sendCode: async (request: SendCodeRequest): Promise<SendCodeResponse> => {
        const res = await apiRequest('POST', '/api/auth/send-code', request);
        return res.json();
    },
    
    verifyCode: async (request: VerifyCodeRequest): Promise<VerifyCodeResponse> => {
        const res = await apiRequest('POST', '/api/auth/verify', request);
        return res.json();
    },
    
    refreshToken: async (request: RefreshTokenRequest): Promise<RefreshTokenResponse> => {
        const res = await apiRequest('POST', '/api/auth/refresh', request);
        return res.json();
    },
    
    getCurrentUser: async (): Promise<User> => {
        const res = await apiRequest('GET', '/api/auth/user/me');
        return res.json();
    },
    
    logout: async (): Promise<void> => {
        await apiRequest('POST', '/api/auth/logout');
    }
};
