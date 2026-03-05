import { Request, Response, NextFunction, Router } from 'express';
import { MonobankService } from '../../../services/monobank.service';
import { PurchaseService } from '../../../application/services/purchase.service';
import { logger } from '../../../infrastructure/logging/logger';
import { AppError } from '../../../shared/errors/app-error';
import { requireAuth } from '../middleware/auth.middleware';

export class MonobankController {
    public readonly router = Router();
    private readonly log = logger.child({ component: 'MonobankController' });

    constructor(
        private readonly monobankService: MonobankService,
        private readonly purchaseService: PurchaseService,
        private readonly webhookUrl: string,
        private readonly frontendUrl: string
    ) {
        this.router.post('/create-invoice', requireAuth, this.createInvoice.bind(this));
        this.router.post('/webhook', this.handleWebhook.bind(this));
    }

    async createInvoice(req: Request, res: Response, next: NextFunction) {
        try {
            const {
                packageId,
                quantity,
                source,
                stationId,
                stationName,
                fuelType,
                fuelName,
                liters,
                price
            } = req.body;

            const userId = (req as any).authUserId;

            if (!userId) {
                throw AppError.unauthorized('User not authenticated');
            }

            // Input validation to prevent DB constraint violations
            if (!packageId || !stationId || !stationName || !fuelName || liters === undefined || price === undefined) {
                this.log.warn({ body: req.body }, 'Missing required purchase details');
                throw AppError.badRequest('Missing required purchase details (station, fuel, or package info)');
            }

            // 1. Create a purchase record (pending)
            let purchaseId: number;
            try {
                purchaseId = await this.purchaseService.createCheckout(userId, {
                    packageId,
                    quantity: quantity || 1,
                    stationId,
                    stationName,
                    fuelType: fuelType || fuelName,
                    fuelName,
                    liters: Number(liters),
                    price: Number(price),
                });
            } catch (dbError: any) {
                this.log.error({ dbError: dbError.message, userId }, 'Failed to create database purchase record');
                throw AppError.internal(`Database error: ${dbError.message}`);
            }

            const purchase = await this.purchaseService.getPurchase(purchaseId);
            if (!purchase) throw AppError.internal('Failed to retrieve created purchase');

            // 2. Create Monobank invoice
            const redirectUrl = source === 'mobile'
                ? 'fuelflow://my-codes'
                : `${this.frontendUrl}/my-codes`;

            // Use a unique merchantPaymentId for each attempt to avoid duplicate ID errors from Monobank
            const merchantPaymentId = `${purchase.id}-${Date.now().toString().slice(-6)}`;

            const invoice = await this.monobankService.createInvoice({
                amount: Math.round(purchase.price * 100), // Monobank expects amount in kopecks
                merchantPaymentId: merchantPaymentId,
                webHookUrl: this.webhookUrl,
                redirectUrl: redirectUrl,
            });

            // 3. Update purchase with latest invoice ID and the unique payment ID used
            await this.purchaseService.updateMonobankInfo(purchase.id, invoice.invoiceId, 'created');

            res.json({
                purchaseId: purchase.id,
                invoiceId: invoice.invoiceId,
                pageUrl: invoice.pageUrl,
            });
        } catch (error: any) {
            this.log.error({
                error: error.message,
                stack: error.stack,
                body: req.body
            }, 'Monobank checkout initiation failed');

            if (error instanceof AppError) {
                return next(error);
            }
            // Ensure any unexpected error is converted to an AppError for the frontend
            next(AppError.internal(error.message || 'Payment initiation failed'));
        }
    }

    async handleWebhook(req: Request, res: Response, next: NextFunction) {
        try {
            const payload = req.body;
            this.log.info({ payload }, 'Received Monobank webhook');

            if (payload.status === 'success') {
                const purchaseId = payload.reference || payload.merchantPaymentId;

                // Fulfill the purchase
                // In Stripe we had simulatePayment, here we do real fulfillment
                await this.purchaseService.fulfillPurchase(purchaseId);

                this.log.info({ purchaseId }, 'Purchase fulfilled via Monobank webhook');
            }

            res.sendStatus(200);
        } catch (error) {
            this.log.error({ error }, 'Error handling Monobank webhook');
            next(error);
        }
    }
}
