import Redis from 'ioredis';

// Redis connection configuration
const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';

if (process.env.NODE_ENV === 'production') {
    const maskedUrl = REDIS_URL.replace(/:[^:@]+@/, ':****@');
    console.log('[Redis] Initializing with URL:', maskedUrl);
}

// Stream names
export const STREAMS = {
    ORDER_EVENTS: 'order-events',
    FULFILLMENT_EVENTS: 'fulfillment-events',
} as const;

// Consumer group names
export const CONSUMER_GROUPS = {
    FULFILLMENT_CONSUMER: 'fulfillment-consumer-group',
} as const;

// Singleton Redis client
let redisClient: Redis | null = null;
let redisSubscriber: Redis | null = null;

/**
 * Get the Redis client instance (creates one if doesn't exist)
 */
export function getRedisClient(): Redis {
    if (!redisClient) {
        redisClient = new Redis(REDIS_URL, {
            maxRetriesPerRequest: 3,
            tls: REDIS_URL.startsWith('rediss://') ? { rejectUnauthorized: false } : undefined,
            retryStrategy: (times) => {
                if (times > 10) {
                    console.error('[Redis] Max retries reached, giving up');
                    return null;
                }
                return Math.min(times * 100, 3000);
            },
            lazyConnect: true,
        });

        redisClient.on('connect', () => {
            console.log('[Redis] Connected to Redis');
        });

        redisClient.on('error', (err) => {
            console.error('[Redis] Connection error:', err.message);
        });

        redisClient.on('close', () => {
            console.log('[Redis] Connection closed');
        });
    }

    return redisClient;
}

/**
 * Get a separate Redis client for subscriptions
 */
export function getRedisSubscriber(): Redis {
    if (!redisSubscriber) {
        redisSubscriber = new Redis(REDIS_URL, {
            maxRetriesPerRequest: 3,
            tls: REDIS_URL.startsWith('rediss://') ? { rejectUnauthorized: false } : undefined,
            lazyConnect: true,
        });
    }
    return redisSubscriber;
}

/**
 * Initialize stream and consumer group
 */
export async function initializeStreams(): Promise<void> {
    const client = getRedisClient();

    try {
        await client.connect();
    } catch (err) {
        // Already connected
    }

    // Create consumer groups for each stream (idempotent)
    for (const streamName of Object.values(STREAMS)) {
        try {
            await client.xgroup('CREATE', streamName, CONSUMER_GROUPS.FULFILLMENT_CONSUMER, '0', 'MKSTREAM');
            console.log(`[Redis] Created consumer group for ${streamName}`);
        } catch (err: any) {
            if (err.message.includes('BUSYGROUP')) {
                // Group already exists, which is fine
                console.log(`[Redis] Consumer group already exists for ${streamName}`);
            } else {
                console.error(`[Redis] Failed to create consumer group for ${streamName}:`, err.message);
            }
        }
    }
}

/**
 * Publish an event to a stream
 */
export async function publishToStream(
    stream: string,
    eventType: string,
    payload: Record<string, unknown>
): Promise<string> {
    const client = getRedisClient();

    const messageId = await client.xadd(
        stream,
        '*', // Auto-generate ID
        'eventType', eventType,
        'payload', JSON.stringify(payload),
        'timestamp', Date.now().toString()
    );

    console.log(`[Redis] Published ${eventType} to ${stream}: ${messageId}`);
    return messageId || '';
}

// Separate client for blocking stream reads to avoid blocking the main client
let redisBlockingClient: Redis | null = null;

/**
 * Get a dedicated Redis client for blocking operations
 */
export function getRedisBlockingClient(): Redis {
    if (!redisBlockingClient) {
        redisBlockingClient = new Redis(REDIS_URL, {
            maxRetriesPerRequest: null, // Blocking operations shouldn't have retries per request
            tls: REDIS_URL.startsWith('rediss://') ? { rejectUnauthorized: false } : undefined,
            lazyConnect: true,
        });
    }
    return redisBlockingClient;
}

/**
 * Read messages from a stream using consumer group
 */
export async function readFromStream(
    stream: string,
    consumerGroup: string,
    consumerName: string,
    count: number = 10,
    blockMs: number = 5000
): Promise<Array<{ id: string; eventType: string; payload: unknown; timestamp: number }>> {
    const client = getRedisBlockingClient();

    try {
        const results = await client.xreadgroup(
            'GROUP', consumerGroup, consumerName,
            'COUNT', count,
            'BLOCK', blockMs,
            'STREAMS', stream,
            '>' // Only read new messages
        ) as [string, [string, string[]][]][] | null;

        if (!results) {
            return [];
        }

        const messages: Array<{ id: string; eventType: string; payload: unknown; timestamp: number }> = [];

        for (const [, streamMessages] of results) {
            for (const [id, fields] of streamMessages) {
                const fieldMap: Record<string, string> = {};
                for (let i = 0; i < fields.length; i += 2) {
                    fieldMap[fields[i]] = fields[i + 1];
                }

                messages.push({
                    id,
                    eventType: fieldMap.eventType || 'UNKNOWN',
                    payload: fieldMap.payload ? JSON.parse(fieldMap.payload) : {},
                    timestamp: parseInt(fieldMap.timestamp || '0', 10),
                });
            }
        }

        return messages;
    } catch (err: any) {
        console.error('[Redis] Error reading from stream:', err.message);
        return [];
    }
}

/**
 * Acknowledge a message as processed
 */
export async function acknowledgeMessage(
    stream: string,
    consumerGroup: string,
    messageId: string
): Promise<void> {
    const client = getRedisClient();
    await client.xack(stream, consumerGroup, messageId);
}

/**
 * Check if Redis is available
 */
export async function isRedisAvailable(): Promise<boolean> {
    try {
        const client = getRedisClient();
        await client.ping();
        return true;
    } catch {
        return false;
    }
}

/**
 * Gracefully close Redis connections
 */
export async function closeRedisConnections(): Promise<void> {
    if (redisClient) {
        await redisClient.quit();
        redisClient = null;
    }
    if (redisSubscriber) {
        await redisSubscriber.quit();
        redisSubscriber = null;
    }
    console.log('[Redis] Connections closed');
}
