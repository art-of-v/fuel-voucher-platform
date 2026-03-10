import * as SecureStore from 'expo-secure-store';

const ACCESS_TOKEN_KEY = 'auth_access_token';
const REFRESH_TOKEN_KEY = 'auth_refresh_token';

export const TokenStorage = {
    async saveTokens(accessToken: string, refreshToken: string) {
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
