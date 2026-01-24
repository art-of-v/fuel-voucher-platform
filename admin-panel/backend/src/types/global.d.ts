// Type declarations for modules without types

declare module 'vite' {
    export function createServer(options: any): Promise<any>;
}

declare module 'nanoid' {
    export function customAlphabet(alphabet: string, size?: number): () => string;
    export function nanoid(size?: number): string;
}

declare module 'node-fetch' {
    const fetch: any;
    export default fetch;
}
