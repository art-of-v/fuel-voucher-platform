/**
 * Purchase Controller
 *
 * Handles purchase creation and payment simulation endpoints.
 * The legacy /session/:sessionId and /:id/complete endpoints have been removed:
 *   - /session/:sessionId was an unauthenticated data-leak endpoint (see ARCHITECTURE_REVIEW.md §6.2)
 *   - /:id/complete was superseded by /simulate in the current purchase flow
 */

import { Router, Request, Response, NextFunction } from "express";
import { PurchaseService } from "../../../application/services/purchase.service";

export class PurchaseController {
    public readonly router: Router;

    constructor(private readonly purchaseService: PurchaseService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.post("/", this.createPurchase.bind(this));
        this.router.get("/my", this.getMyPurchases.bind(this));
        this.router.post("/simulate", this.simulatePayment.bind(this));
    }

    private async createPurchase(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).userId;
            const { packageId, stationId, stationName, fuelType, fuelName, liters, quantity, price } =
                req.body;

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
            const userId = (req as any).userId;
            const purchases = await this.purchaseService.getUserPurchases(userId);
            res.json(purchases);
        } catch (error) {
            next(error);
        }
    }

    private async simulatePayment(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { purchaseId, scenario } = req.body;
            const id = parseInt(purchaseId, 10);
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
 * Thin wrapper around createCheckout — kept separate for semantic URL clarity.
 */
export class CheckoutController {
    public readonly router: Router;

    constructor(private readonly purchaseService: PurchaseService) {
        this.router = Router();
        this.router.post("/", this.createCheckout.bind(this));
    }

    private async createCheckout(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).userId;
            const { packageId, stationId, stationName, fuelType, fuelName, liters, quantity, price } =
                req.body;

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
