/**
 * Structured Logger
 * 
 * Provides consistent, structured logging across the application using pino.
 * Features:
 * - JSON format in production
 * - Pretty print in development
 * - Request context support
 * - Child loggers for components
 */

import pino, { Logger as PinoLogger } from 'pino';
import { config } from '../../config';

/**
 * Logger configuration based on environment
 */
const loggerConfig: pino.LoggerOptions = {
    level: config.logging.level,
    base: {
        service: config.app.name,
        version: config.app.version,
        env: config.app.env,
    },
    timestamp: pino.stdTimeFunctions.isoTime,
    formatters: {
        level: (label) => ({ level: label }),
    },
};

/**
 * Note: pino-pretty transport is disabled in production builds due to ESM/CJS
 * compatibility issues with esbuild bundling. In development, logs are still
 * structured JSON which can be piped to pino-pretty manually if desired.
 * 
 * To pretty print locally: npm run dev | npx pino-pretty
 */

/**
 * Root logger instance
 */
export const logger = pino(loggerConfig) as PinoLogger;

/**
 * Create a child logger for a specific component
 */
export function createLogger(component: string): PinoLogger {
    return logger.child({ component });
}

/**
 * Logger interface for dependency injection
 */
export interface ILogger {
    trace(msg: string, ...args: unknown[]): void;
    trace(obj: object, msg?: string, ...args: unknown[]): void;
    debug(msg: string, ...args: unknown[]): void;
    debug(obj: object, msg?: string, ...args: unknown[]): void;
    info(msg: string, ...args: unknown[]): void;
    info(obj: object, msg?: string, ...args: unknown[]): void;
    warn(msg: string, ...args: unknown[]): void;
    warn(obj: object, msg?: string, ...args: unknown[]): void;
    error(msg: string, ...args: unknown[]): void;
    error(obj: object, msg?: string, ...args: unknown[]): void;
    fatal(msg: string, ...args: unknown[]): void;
    fatal(obj: object, msg?: string, ...args: unknown[]): void;
    child(bindings: Record<string, unknown>): ILogger;
}

/**
 * Request context logger bindings
 */
export interface RequestLogContext {
    requestId?: string;
    userId?: string;
    path?: string;
    method?: string;
}

/**
 * Create a logger with request context
 */
export function createRequestLogger(context: RequestLogContext): PinoLogger {
    return logger.child(context);
}

// Component-specific loggers (pre-created for convenience)
export const loggers = {
    http: createLogger('http'),
    auth: createLogger('auth'),
    database: createLogger('database'),
    redis: createLogger('redis'),
    fulfillment: createLogger('fulfillment'),
    import: createLogger('import'),
    payment: createLogger('payment'),
    notification: createLogger('notification'),
};

// Default export
export default logger;
