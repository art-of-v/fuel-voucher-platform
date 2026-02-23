/**
 * Purchase Controller
 * 
 * Handles purchase and payment-related HTTP endpoints.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { PurchaseService } from '../../../application/services/purchase.service';
import { requireAuth } from '../middleware/auth.middleware';

export class PurchaseController {
    public readonly router: Router;

    constructor(private readonly purchaseService: PurchaseService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        // Create purchase
        this.router.post('/', requireAuth, this.createPurchase.bind(this));

        // Get my purchases
        this.router.get('/my', requireAuth, this.getMyPurchases.bind(this));

        // Get purchases by session ID (legacy)
        this.router.get('/session/:sessionId', this.getPurchasesBySession.bind(this));

        // Complete purchase
        this.router.post('/:id/complete', this.completePurchase.bind(this));

        // Simulate payment
        this.router.post('/simulate', requireAuth, this.simulatePayment.bind(this));
    }

    private async createPurchase(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).authUserId!;
            const { packageId, stationId, stationName, fuelType, fuelName, liters, quantity, price } = req.body;

            const purchaseId = await this.purchaseService.createCheckout(userId, {
                packageId,
                stationId,
                stationName,
                fuelType,
                fuelName,
                liters,
                quantity,
                price,
            });

            res.json({ purchaseId });
        } catch (error) {
            next(error);
        }
    }

    private async getMyPurchases(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).authUserId!;
            const purchases = await this.purchaseService.getUserPurchases(userId);
            res.json(purchases);
        } catch (error) {
            next(error);
        }
    }

    private async getPurchasesBySession(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { sessionId } = req.params;
            const purchases = await this.purchaseService.getUserPurchases(sessionId);
            res.json(purchases);
        } catch (error) {
            next(error);
        }
    }

    private async completePurchase(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { id } = req.params;
            const purchaseId = parseInt(id);

            const purchase = await this.purchaseService.completePurchase(purchaseId);
            res.json(purchase);
        } catch (error) {
            next(error);
        }
    }

    private async simulatePayment(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { purchaseId, scenario } = req.body;
            const id = parseInt(purchaseId);

            const result = await this.purchaseService.simulatePayment(id, scenario);
            res.json(result);
        } catch (error) {
            next(error);
        }
    }
}

/**
 * Checkout Controller
 * 
 * Handles checkout flow.
 */
export class CheckoutController {
    public readonly router: Router;

    constructor(private readonly purchaseService: PurchaseService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.post('/', requireAuth, this.createCheckout.bind(this));
    }

    private async createCheckout(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { packageId, stationId, stationName, fuelType, fuelName, liters, quantity, price } = req.body;
            const userId = (req as any).authUserId!;

            const purchaseId = await this.purchaseService.createCheckout(userId, {
                packageId,
                stationId,
                stationName,
                fuelType,
                fuelName,
                liters,
                quantity,
                price,
            });

            res.json({ purchaseId });
        } catch (error) {
            next(error);
        }
    }
}
