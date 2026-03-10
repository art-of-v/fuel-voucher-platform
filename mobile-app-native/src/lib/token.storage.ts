import * as SecureStore from 'expo-secure-store';

const ACCESS_TOKEN_KEY = 'auth_access_token';
const REFRESH_TOKEN_KEY = 'auth_refresh_token';

export const TokenStorage = {
    async saveTokens(accessToken: string, refreshToken: string) {
        if (!accessToken || typeof accessToken !== 'string') {
            throw new Error('Invalid accessToken: SecureStore requires a non-empty string');
        }
        if (!refreshToken || typeof refreshToken !== 'string') {
            throw new Error('Invalid refreshToken: SecureStore requires a non-empty string');
        }
        await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, accessToken);
        await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, refreshToken);
    },

    async getAccessToken() {
        return await SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
    },

    async getRefreshToken() {
        return await SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
    },

    async clearTokens() {
        await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
        await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
    }
};
