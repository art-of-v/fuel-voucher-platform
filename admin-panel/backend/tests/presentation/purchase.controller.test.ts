
import { describe, it, expect, vi, beforeEach } from 'vitest';
import request from 'supertest';
import { PurchaseController } from '../../src/presentation/http/controllers/purchase.controller';
import { createApp } from './app-helper';

// Mock auth middleware to bypass checks
vi.mock('../../src/presentation/http/middleware/auth.middleware', () => ({
    requireAuth: (req: any, res: any, next: any) => {
        req.authUserId = 'u1';
        next();
    }
}));

describe('Purchase Controller', () => {
    let purchaseController: PurchaseController;
    let app: any;
    const mockPurchaseService = {
        createCheckout: vi.fn(),
        getUserPurchases: vi.fn(),
        completePurchase: vi.fn(),
        simulatePayment: vi.fn(),
    };

    beforeEach(() => {
        vi.clearAllMocks();
        purchaseController = new PurchaseController(mockPurchaseService as any);
        app = createApp(purchaseController.router);
    });

    describe('POST /', () => {
        it('should create purchase successfully', async () => {
            mockPurchaseService.createCheckout.mockResolvedValue(101);

            const payload = {
                packageId: 'p1', stationId: 's1', stationName: 'OKKO',
                fuelType: '95', fuelName: 'A-95', liters: 10, price: 500
            };

            const res = await request(app).post('/').send(payload);

            expect(res.status).toBe(200);
            expect(res.body.purchaseId).toBe(101);
            expect(mockPurchaseService.createCheckout).toHaveBeenCalledWith('u1', payload);
        });

        it('should handle service errors', async () => {
            mockPurchaseService.createCheckout.mockRejectedValue(new Error('Failed'));
            const res = await request(app).post('/').send({});

            expect(res.status).toBe(500);
            // StatusCode depends on error type handling in errorHandler (mocked app helper handles generic as 500)
        });
    });

    describe('GET /my', () => {
        it('should return user purchases', async () => {
            const purchases = [{ id: 1, status: 'paid' }];
            mockPurchaseService.getUserPurchases.mockResolvedValue(purchases);

            const res = await request(app).get('/my');

            expect(res.status).toBe(200);
            expect(res.body).toEqual(purchases);
            expect(mockPurchaseService.getUserPurchases).toHaveBeenCalledWith('u1');
        });
    });

    describe('POST /:id/complete', () => {
        it('should complete purchase', async () => {
            mockPurchaseService.completePurchase.mockResolvedValue({ id: 1, status: 'delivered' });

            const res = await request(app).post('/1/complete');

            expect(res.status).toBe(200);
            expect(mockPurchaseService.completePurchase).toHaveBeenCalledWith(1);
        });
    });

    describe('POST /simulate', () => {
        it('should simulate payment', async () => {
            const result = { status: 'success' };
            mockPurchaseService.simulatePayment.mockResolvedValue(result);

            const res = await request(app).post('/simulate').send({ purchaseId: 101, scenario: 'success' });

            expect(res.body).toEqual(result);
            expect(mockPurchaseService.simulatePayment).toHaveBeenCalledWith(101, 'success');
        });
    });
});
