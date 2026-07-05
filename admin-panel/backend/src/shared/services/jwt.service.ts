import jwt from 'jsonwebtoken';
import { config } from '../../config';

export class JwtService {
    /**
     * Generate a short-lived access token (e.g., 15 minutes)
     */
    static generateAccessToken(userId: string, deviceId: string): string {
        return jwt.sign(
            { sub: userId, deviceId },
            config.session.secret,
            { expiresIn: '15m' }
        );
    }

    /**
     * Generate a long-lived refresh token (e.g., 30 days)
     */
    static generateRefreshToken(userId: string, deviceId: string): string {
        return jwt.sign(
            { sub: userId, deviceId },
            config.session.secret,
            { expiresIn: '30d' }
        );
    }

    /**
     * Verify and decode a token
     */
    static verifyToken(token: string): any {
        try {
            return jwt.verify(token, config.session.secret);
        } catch (err) {
            return null;
        }
    }
}
