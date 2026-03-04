/**
 * Voucher Controller
 * 
 * Handles voucher-related HTTP endpoints.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { VoucherService } from '../../../application/services/voucher.service';
import { VoucherStatus } from '../../../domain/repositories/voucher.repository';
import { requireAuth } from '../middleware/auth.middleware';

export class VoucherController {
    public readonly router: Router;

    constructor(private readonly voucherService: VoucherService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        // Get my vouchers
        this.router.get('/my', requireAuth, this.getMyVouchers.bind(this));

        // Get inventory
        this.router.get('/inventory', this.getInventory.bind(this));

        // Mark voucher as used
        this.router.patch('/:id/mark-used', requireAuth, this.markUsed.bind(this));

        // Restore voucher
        this.router.patch('/:id/restore', requireAuth, this.restore.bind(this));
    }

    private async getMyVouchers(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).authUserId!;
            const vouchers = await this.voucherService.getUserVouchers(userId);
            res.json(vouchers);
        } catch (error) {
            next(error);
        }
    }

    private async getInventory(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const inventory = await this.voucherService.getInventory();
            res.json(inventory);
        } catch (error) {
            next(error);
        }
    }

    private async markUsed(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const userId = (req as any).authUserId as string | undefined;

            const voucher = await this.voucherService.getVoucherById(id);
            if (!voucher) {
                res.status(404).json({ message: 'Voucher not found' });
                return;
            }

            // Verify ownership — prevent one user marking another user's voucher used
            if (userId && voucher.assignedToUserId && voucher.assignedToUserId !== userId) {
                res.status(403).json({ message: 'Forbidden' });
                return;
            }

            await this.voucherService.updateVoucher(id, { status: 'used' as any });
            res.json({ message: 'Voucher marked as used', status: 'used' });
        } catch (error) {
            next(error);
        }
    }

    private async restore(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const voucher = await this.voucherService.getVoucherById(id);
            if (!voucher) {
                res.status(404).json({ message: 'Voucher not found' });
                return;
            }

            await this.voucherService.updateVoucher(id, { status: 'available' as any });
            res.json({ message: 'Voucher restored', status: 'available' });
        } catch (error) {
            next(error);
        }
    }
}

/**
 * Admin Voucher Controller
 * 
 * Handles admin voucher management endpoints.
 */
export class AdminVoucherController {
    public readonly router: Router;

    constructor(private readonly voucherService: VoucherService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        // Get vouchers with filters (use ?status=available for available vouchers)
        this.router.get('/', this.getVouchers.bind(this));

        // Bulk actions
        this.router.post('/bulk-action', this.bulkAction.bind(this));

        // Mark voucher as used
        this.router.patch('/:id/mark-used', this.markUsed.bind(this));

        // Restore voucher to available
        this.router.patch('/:id/restore', this.restore.bind(this));

        // Get voucher by ID
        this.router.get('/:id', this.getVoucherById.bind(this));

        // Update voucher
        this.router.put('/:id', this.updateVoucher.bind(this));
        this.router.patch('/:id', this.updateVoucher.bind(this));

        // Delete voucher
        this.router.delete('/:id', this.deleteVoucher.bind(this));

        // Delete all vouchers
        this.router.delete('/', this.deleteAllVouchers.bind(this));
    }

    private async getVouchers(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { status, provider, fuelType, amount, expirationDate, limit, offset, page, sortBy, sortDirection } = req.query;

            const filters = {
                status: status as VoucherStatus | undefined,
                provider: provider as string | undefined,
                fuelType: fuelType as string | undefined,
                amount: amount ? parseInt(amount as string) : undefined,
                expirationDate: expirationDate as string | undefined,
            };

            // Support both offset and page-based pagination
            const limitNum = limit ? parseInt(limit as string) : 50;
            const offsetNum = offset
                ? parseInt(offset as string)
                : page
                    ? (parseInt(page as string) - 1) * limitNum
                    : 0;

            const pagination = {
                limit: limitNum,
                offset: offsetNum,
            };

            const sort = sortBy ? {
                field: sortBy as string,
                direction: (sortDirection as 'asc' | 'desc') || 'desc',
            } : undefined;

            const result = await this.voucherService.getVouchers(filters, pagination, sort);
            res.json(result);
        } catch (error) {
            next(error);
        }
    }


    private async getVoucherById(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const voucher = await this.voucherService.getVoucherById(id);

            if (!voucher) {
                res.status(404).json({ error: 'Voucher not found' });
                return;
            }

            res.json(voucher);
        } catch (error) {
            next(error);
        }
    }

    private async updateVoucher(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const voucher = await this.voucherService.updateVoucher(id, req.body);
            res.json(voucher);
        } catch (error) {
            next(error);
        }
    }

    private async markUsed(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const voucher = await this.voucherService.getVoucherById(id);

            if (!voucher) {
                res.status(404).json({ message: 'Voucher not found' });
                return;
            }

            await this.voucherService.updateVoucher(id, { status: 'used' as any });
            res.json({ message: 'Voucher marked as used', status: 'used' });
        } catch (error) {
            next(error);
        }
    }

    private async restore(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const voucher = await this.voucherService.getVoucherById(id);

            if (!voucher) {
                res.status(404).json({ message: 'Voucher not found' });
                return;
            }

            await this.voucherService.updateVoucher(id, { status: 'available' as any });
            res.json({ message: 'Voucher restored', status: 'available' });
        } catch (error) {
            next(error);
        }
    }

    private async bulkAction(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { action, ids, targetUserId } = req.body;

            // Handle delete all action
            if (action === 'delete_all') {
                await this.voucherService.deleteAllVouchers();
                res.json({ success: true });
                return;
            }

            if (!Array.isArray(ids) || ids.length === 0) {
                res.status(400).json({ error: 'No IDs provided' });
                return;
            }

            // Handle different actions
            switch (action) {
                case 'activate':
                    await Promise.all(ids.map(id => this.voucherService.updateVoucher(id, { status: 'available' as any })));
                    break;
                case 'expire':
                    await Promise.all(ids.map(id => this.voucherService.updateVoucher(id, { status: 'expired' as any })));
                    break;
                case 'assign':
                    if (!targetUserId) {
                        res.status(400).json({ error: 'Target User ID required' });
                        return;
                    }
                    await Promise.all(ids.map(id => this.voucherService.updateVoucher(id, {
                        status: 'assigned' as any,
                        assignedToUserId: targetUserId
                    })));
                    break;
                case 'delete':
                    await Promise.all(ids.map(id => this.voucherService.deleteVoucher(id)));
                    break;
                default:
                    res.status(400).json({ error: `Unknown action: ${action}` });
                    return;
            }

            res.json({ success: true, count: ids.length });
        } catch (error) {
            next(error);
        }
    }

    private async deleteVoucher(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            await this.voucherService.deleteVoucher(id);
            res.json({ success: true });
        } catch (error) {
            next(error);
        }
    }

    private async deleteAllVouchers(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            await this.voucherService.deleteAllVouchers();
            res.json({ success: true });
        } catch (error) {
            next(error);
        }
    }
}

/**
 * Test Voucher Controller
 * 
 * Test endpoints for manual voucher assignment.
 */
export class TestVoucherController {
    public readonly router: Router;

    constructor(private readonly voucherService: VoucherService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.post('/assign-vouchers', this.assignVouchers.bind(this));
    }

    private async assignVouchers(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { userId, items } = req.body;

            if (!userId || !items || !Array.isArray(items)) {
                res.status(400).json({
                    error: 'userId and items array required',
                    example: {
                        userId: 'a0e45e0e-b75b-43f4-abf7-f220c9ba7b59',
                        items: [{ station: 'OKKO', fuelType: 'A-95', liters: 10, quantity: 2 }]
                    }
                });
                return;
            }

            console.log(`[TEST] Manually assigning vouchers to user: ${userId}`);
            const results = await this.voucherService.assignVouchersManually(userId, items);

            res.json({ success: true, userId, results });
        } catch (error) {
            next(error);
        }
    }
}
