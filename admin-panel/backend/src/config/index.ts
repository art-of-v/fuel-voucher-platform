/**
 * Centralized Configuration
 * 
 * All environment variables and configuration values are defined here.
 * This provides:
 * - Type safety
 * - Default values
 * - Single source of truth
 * - Validation on startup
 */

function requireEnv(key: string, defaultValue?: string): string {
    const value = process.env[key] ?? defaultValue;
    if (value === undefined) {
        throw new Error(`Missing required environment variable: ${key}`);
    }
    return value;
}

function optionalEnv(key: string, defaultValue: string): string {
    return process.env[key] ?? defaultValue;
}

function optionalEnvInt(key: string, defaultValue: number): number {
    const value = process.env[key];
    if (value === undefined) return defaultValue;
    const parsed = parseInt(value, 10);
    if (isNaN(parsed)) {
        console.warn(`Invalid integer for ${key}: ${value}, using default ${defaultValue}`);
        return defaultValue;
    }
    return parsed;
}

function optionalEnvBool(key: string, defaultValue: boolean): boolean {
    const value = process.env[key];
    if (value === undefined) return defaultValue;
    return value.toLowerCase() === 'true' || value === '1';
}

export const config = {
    /**
     * Application settings
     */
    app: {
        name: 'fuel-flow-api',
        version: process.env.npm_package_version || '1.0.0',
        port: optionalEnvInt('PORT', 4000),
        env: optionalEnv('NODE_ENV', 'development'),
        isDev: optionalEnv('NODE_ENV', 'development') !== 'production',
        isProd: optionalEnv('NODE_ENV', 'development') === 'production',

        // Development fallback user ID (for testing without auth)
        devUserId: optionalEnv('DEV_USER_ID', 'd366f82a-e65c-4110-bf20-ab2f44750cfe'),
    },

    /**
     * Database configuration
     */
    database: {
        url: requireEnv('DATABASE_URL', 'postgresql://postgres:postgres@localhost:5432/fuel_db'),
        poolMin: optionalEnvInt('DB_POOL_MIN', 2),
        poolMax: optionalEnvInt('DB_POOL_MAX', 10),
        logQueries: optionalEnvBool('DB_LOG_QUERIES', false),
    },

    /**
     * Redis configuration
     */
    redis: {
        url: optionalEnv('REDIS_URL', 'redis://localhost:6379'),
        streamBlockMs: optionalEnvInt('REDIS_STREAM_BLOCK_MS', 5000),
        consumerPollIntervalMs: optionalEnvInt('REDIS_CONSUMER_POLL_MS', 5000),
        maxRetries: optionalEnvInt('REDIS_MAX_RETRIES', 10),
    },

    /**
     * Session configuration
     */
    session: {
        secret: optionalEnv('SESSION_SECRET', 'fuel-flow-secret-key-change-in-production'),
        maxAgeMs: optionalEnvInt('SESSION_MAX_AGE_MS', 24 * 60 * 60 * 1000), // 24 hours
        secure: optionalEnvBool('SESSION_SECURE', false),
    },

    /**
     * Stripe payment configuration
     */
    stripe: {
        secretKey: optionalEnv('STRIPE_SECRET_KEY', ''),
        publishableKey: optionalEnv('STRIPE_PUBLISHABLE_KEY', ''),
        webhookSecret: optionalEnv('STRIPE_WEBHOOK_SECRET', ''),
        enabled: !!process.env.STRIPE_SECRET_KEY,
    },

    /**
     * Twilio SMS configuration
     */
    twilio: {
        accountSid: optionalEnv('TWILIO_ACCOUNT_SID', ''),
        authToken: optionalEnv('TWILIO_AUTH_TOKEN', ''),
        phoneNumber: optionalEnv('TWILIO_PHONE_NUMBER', ''),
        enabled: !!process.env.TWILIO_ACCOUNT_SID,
    },

    /**
     * Google Gemini AI configuration
     */
    gemini: {
        apiKey: optionalEnv('GEMINI_API_KEY', ''),
        model: optionalEnv('GEMINI_MODEL', 'gemini-2.0-flash-exp'),
        enabled: !!process.env.GEMINI_API_KEY,
    },

    /**
     * Rate limiting
     */
    rateLimiting: {
        otp: {
            windowMs: optionalEnvInt('RATE_LIMIT_OTP_WINDOW_MS', 60000), // 1 minute
            maxRequests: optionalEnvInt('RATE_LIMIT_OTP_MAX', 3),
        },
        otpVerify: {
            windowMs: optionalEnvInt('RATE_LIMIT_OTP_VERIFY_WINDOW_MS', 300000), // 5 minutes
            maxRequests: optionalEnvInt('RATE_LIMIT_OTP_VERIFY_MAX', 5),
        },
        api: {
            windowMs: optionalEnvInt('RATE_LIMIT_API_WINDOW_MS', 60000),
            maxRequests: optionalEnvInt('RATE_LIMIT_API_MAX', 100),
        },
    },

    /**
     * Logging configuration
     */
    logging: {
        level: optionalEnv('LOG_LEVEL', 'info'),
        prettyPrint: optionalEnvBool('LOG_PRETTY', true),
    },

    /**
     * File upload configuration
     */
    upload: {
        maxFileSizeMb: optionalEnvInt('UPLOAD_MAX_SIZE_MB', 50),
        allowedMimeTypes: ['application/pdf', 'image/png', 'image/jpeg'],
        uploadDir: optionalEnv('UPLOAD_DIR', 'uploads'),
    },
} as const;

// Type exports
export type AppConfig = typeof config;
export type DatabaseConfig = typeof config.database;
export type RedisConfig = typeof config.redis;

// Validation on import
if (config.app.isProd) {
    // Production validations
    if (config.session.secret === 'fuel-flow-secret-key-change-in-production') {
        console.error('⚠️  WARNING: Using default session secret in production!');
    }
    if (!config.stripe.enabled) {
        console.warn('⚠️  Stripe is not configured');
    }
    if (!config.twilio.enabled) {
        console.warn('⚠️  Twilio is not configured');
    }
}
