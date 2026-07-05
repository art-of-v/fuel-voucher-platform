/**
 * Redis Infrastructure
 *
 * Handles Redis Streams for async order fulfillment.
 *
 * Key design decisions:
 * - Separate blocking client so XREADGROUP BLOCK doesn't block the main client
 * - PEL recovery via XAUTOCLAIM — handles crashed consumers automatically
 * - Console logging replaced with pino
 */

import Redis from 'ioredis';
import { logger } from '../../infrastructure/logging/logger';

const log = logger.child({ component: 'Redis' });

const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';

// Stream names
export const STREAMS = {
    ORDER_EVENTS: 'order-events',
    FULFILLMENT_EVENTS: 'fulfillment-events',
} as const;

// Consumer group names
export const CONSUMER_GROUPS = {
    FULFILLMENT_CONSUMER: 'fulfillment-consumer-group',
} as const;

// Messages idle longer than this are considered abandoned and will be reclaimed
export const PEL_CLAIM_IDLE_MS = 30_000; // 30 seconds

// ─── Connection factory ────────────────────────────────────────────────────

function makeClient(options: { blocking?: boolean } = {}): Redis {
    const isTLS = REDIS_URL.startsWith('rediss://');
    return new Redis(REDIS_URL, {
        // Blocking clients must not have per-request retries — they use BLOCK semantics
        maxRetriesPerRequest: options.blocking ? null : 3,
        tls: isTLS ? { rejectUnauthorized: false } : undefined,
        retryStrategy: (times) => {
            if (times > 10) return null;
            return Math.min(times * 100, 3_000);
        },
        lazyConnect: true,
    });
}

// ─── Singletons ────────────────────────────────────────────────────────────

let redisClient: Redis | null = null;
let redisSubscriber: Redis | null = null;
let redisBlockingClient: Redis | null = null;

export function getRedisClient(): Redis {
    if (!redisClient) {
        redisClient = makeClient();
        redisClient.on('connect', () => log.info('Connected'));
        redisClient.on('error', (err) => log.error({ err: err.message }, 'Connection error'));
        redisClient.on('close', () => log.warn('Connection closed'));
    }
    return redisClient;
}

export function getRedisSubscriber(): Redis {
    if (!redisSubscriber) {
        redisSubscriber = makeClient();
    }
    return redisSubscriber;
}

export function getRedisBlockingClient(): Redis {
    if (!redisBlockingClient) {
        redisBlockingClient = makeClient({ blocking: true });
    }
    return redisBlockingClient;
}

// ─── Stream management ─────────────────────────────────────────────────────

/**
 * Create consumer groups for all streams (idempotent).
 * Uses MKSTREAM so the stream is created if it doesn't exist yet.
 */
export async function initializeStreams(): Promise<void> {
    const client = getRedisClient();

    try {
        await client.connect();
    } catch {
        // Already connected — ignore
    }

    for (const streamName of Object.values(STREAMS)) {
        try {
            await client.xgroup('CREATE', streamName, CONSUMER_GROUPS.FULFILLMENT_CONSUMER, '0', 'MKSTREAM');
            log.info({ stream: streamName }, 'Consumer group created');
        } catch (err: any) {
            if (err.message.includes('BUSYGROUP')) {
                log.debug({ stream: streamName }, 'Consumer group already exists');
            } else {
                log.error({ err: err.message, stream: streamName }, 'Failed to create consumer group');
                throw err;
            }
        }
    }
}

// ─── Publishing ────────────────────────────────────────────────────────────

/**
 * Publish an event to a Redis Stream.
 * Returns the auto-generated stream message ID (format: "timestamp-seq").
 */
export async function publishToStream(
    stream: string,
    eventType: string,
    payload: Record<string, unknown>
): Promise<string> {
    const client = getRedisClient();

    const messageId = await client.xadd(
        stream,
        '*',
        'eventType', eventType,
        'payload', JSON.stringify(payload),
        'timestamp', Date.now().toString()
    );

    log.debug({ stream, eventType, messageId }, 'Event published');
    return messageId || '';
}

// ─── Reading ───────────────────────────────────────────────────────────────

export interface StreamMessage {
    id: string;
    eventType: string;
    payload: unknown;
    timestamp: number;
}

/**
 * Read new messages from a stream via consumer group.
 *
 * Uses XREADGROUP with BLOCK so the connection sleeps until a message arrives —
 * no busy-wait polling. The blocking client is separate from the main client to
 * avoid blocking unrelated Redis operations.
 *
 * count should be 1 for strict FIFO processing.
 */
export async function readFromStream(
    stream: string,
    consumerGroup: string,
    consumerName: string,
    count: number = 1,
    blockMs: number = 5_000
): Promise<StreamMessage[]> {
    const client = getRedisBlockingClient();

    try {
        await client.connect().catch(() => { /* already connected */ });

        const results = await client.xreadgroup(
            'GROUP', consumerGroup, consumerName,
            'COUNT', count,
            'BLOCK', blockMs,
            'STREAMS', stream,
            '>'  // '>' means "only new, undelivered messages"
        ) as [string, [string, string[]][]][] | null;

        if (!results) return [];

        return parseStreamResults(results);
    } catch (err: any) {
        log.error({ err: err.message, stream }, 'Error reading from stream');
        return [];
    }
}

/**
 * Reclaim messages that have been pending (unacknowledged) for too long.
 *
 * This is the PEL recovery mechanism: if a consumer crashes after receiving
 * a message but before acknowledging it, that message stays in the Pending
 * Entry List (PEL) forever — unless another consumer claims it.
 *
 * Call this on startup and periodically so no messages are lost.
 *
 * Uses XAUTOCLAIM (Redis ≥ 7.0) which atomically finds AND claims idle messages.
 */
export async function reclaimAbandonedMessages(
    stream: string,
    consumerGroup: string,
    consumerName: string,
    count: number = 10,
    idleMs: number = PEL_CLAIM_IDLE_MS
): Promise<StreamMessage[]> {
    const client = getRedisClient();

    try {
        // XAUTOCLAIM: atomically claims messages idle > idleMs ms and delivers them to consumerName
        // Signature: XAUTOCLAIM key group consumer min-idle-time start [COUNT count]
        const result = await client.call(
            'XAUTOCLAIM',
            stream,
            consumerGroup,
            consumerName,
            idleMs.toString(),
            '0-0',   // start from the beginning of the PEL
            'COUNT', count.toString()
        ) as [string, [string, string[]][]] | null;

        if (!result || !result[1] || result[1].length === 0) {
            return [];
        }

        const claimed = parseStreamResults([[stream, result[1]]]);
        if (claimed.length > 0) {
            log.warn({ count: claimed.length, stream }, 'Reclaimed abandoned PEL messages');
        }

        return claimed;
    } catch (err: any) {
        // XAUTOCLAIM requires Redis 7.0+ — fall back gracefully on older Redis
        if (err.message.includes('ERR unknown command')) {
            log.warn('XAUTOCLAIM not available (Redis < 7.0) — PEL recovery disabled');
            return [];
        }
        log.error({ err: err.message, stream }, 'PEL reclaim error');
        return [];
    }
}

// ─── Acknowledging ─────────────────────────────────────────────────────────

/**
 * Acknowledge a message so Redis removes it from the PEL.
 * Always call this after successfully processing a message.
 */
export async function acknowledgeMessage(
    stream: string,
    consumerGroup: string,
    messageId: string
): Promise<void> {
    const client = getRedisClient();
    await client.xack(stream, consumerGroup, messageId);
}

// ─── Health ────────────────────────────────────────────────────────────────

export async function isRedisAvailable(): Promise<boolean> {
    try {
        const client = getRedisClient();
        await client.connect().catch(() => { /* already connected */ });
        await client.ping();
        return true;
    } catch {
        return false;
    }
}

// ─── Cleanup ───────────────────────────────────────────────────────────────

export async function closeRedisConnections(): Promise<void> {
    const clients = [redisClient, redisSubscriber, redisBlockingClient].filter(Boolean) as Redis[];
    await Promise.all(clients.map(c => c.quit()));
    redisClient = null;
    redisSubscriber = null;
    redisBlockingClient = null;
    log.info('All Redis connections closed');
}

// ─── Helpers ───────────────────────────────────────────────────────────────

function parseStreamResults(
    results: [string, [string, string[][]]][] | any
): StreamMessage[] {
    const messages: StreamMessage[] = [];

    for (const [, streamMessages] of results) {
        if (!streamMessages) continue;
        for (const [id, fields] of streamMessages) {
            if (!fields) continue;
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
}
