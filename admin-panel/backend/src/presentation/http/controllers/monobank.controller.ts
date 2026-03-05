import { Request, Response, NextFunction, Router } from 'express';
import { MonobankService } from '../../../services/monobank.service';
import { PurchaseService } from '../../../application/services/purchase.service';
import { logger } from '../../../infrastructure/logging/logger';
import { AppError } from '../../../shared/errors/app-error';

export class MonobankController {
    public readonly router = Router();
    private readonly log = logger.child({ component: 'MonobankController' });

    constructor(
        private readonly monobankService: MonobankService,
        private readonly purchaseService: PurchaseService,
        private readonly webhookUrl: string,
        private readonly frontendUrl: string
    ) {
        this.router.post('/create-invoice', this.createInvoice.bind(this));
        this.router.post('/webhook', this.handleWebhook.bind(this));
    }

    async createInvoice(req: Request, res: Response, next: NextFunction) {
        try {
            const { packageId, quantity, source } = req.body;
            const userId = (req.session as any).userId;

            if (!userId) {
                throw AppError.unauthorized('User not authenticated');
            }

            // 1. Create a purchase record (pending)
            const purchaseId = await this.purchaseService.createCheckout(userId, {
                packageId,
                quantity,
                stationId: req.body.stationId,
                stationName: req.body.stationName,
                fuelType: req.body.fuelType,
                fuelName: req.body.fuelName,
                liters: req.body.liters,
                price: req.body.price,
            });

            const purchase = await this.purchaseService.getPurchase(purchaseId);
            if (!purchase) throw AppError.internal('Failed to retrieve created purchase');

            // 2. Create Monobank invoice
            // If from mobile, redirect back into the app using its deep link scheme.
            // If from another source (e.g., testing via Swagger/Postman), use the frontend URL.
            const redirectUrl = source === 'mobile'
                ? 'fuelflow://my-codes'
                : `${this.frontendUrl}/my-codes`;

            const invoice = await this.monobankService.createInvoice({
                amount: Math.round(purchase.price * 100), // Monobank expects amount in kopecks
                merchantPaymentId: String(purchase.id),
                webHookUrl: this.webhookUrl,
                redirectUrl: redirectUrl,
            });

            // 3. Update purchase with invoice ID
            await this.purchaseService.updateMonobankInfo(purchase.id, invoice.invoiceId, 'created');

            res.json({
                purchaseId: purchase.id,
                invoiceId: invoice.invoiceId,
                pageUrl: invoice.pageUrl,
            });
        } catch (error) {
            next(error);
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
