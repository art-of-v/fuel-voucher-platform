/**
 * Base Repository Interface
 * 
 * Common repository operations that all repositories should implement.
 */

export interface IBaseRepository<T, ID> {
    /**
     * Find entity by ID
     */
    findById(id: ID): Promise<T | null>;

    /**
     * Check if entity exists
     */
    exists(id: ID): Promise<boolean>;

    /**
     * Save (create or update) entity
     */
    save(entity: T): Promise<T>;

    /**
     * Delete entity by ID
     */
    delete(id: ID): Promise<void>;
}

/**
 * Pagination options
 */
export interface PaginationOptions {
    limit: number;
    offset: number;
}

/**
 * Paginated result
 */
export interface PaginatedResult<T> {
    data: T[];
    total: number;
    limit: number;
    offset: number;
    hasMore?: boolean;
}

/**
 * Sort options
 */
export interface SortOptions {
    field: string;
    direction: 'asc' | 'desc';
}
