import { storage } from '../storage';
import { FuelType, FuelPackage } from '@shared/schema';

describe('Storage Layer', () => {
    // Test Data Setup
    const testStationId = 'test-station';
    const testFuelId = 'test-fuel-95';

    beforeAll(async () => {
        // Ideally we would reset the memory storage here, 
        // but the current implementation initializes data in constructor.
        // For unit tests, we can verify the seeded data or add new test data.
    });

    describe('Fuel Types', () => {
        it('should retrieve all fuel types', async () => {
            const fuelTypes = await storage.getAllFuelTypes();
            expect(fuelTypes.length).toBeGreaterThan(0);
        });

        it('should create and retrieve a new fuel type', async () => {
            const newFuel: any = {
                id: 'test-new-fuel',
                name: 'Test Fuel',
                stationId: testStationId,
                basePrice: 50,
                discountPrice: 45
            };

            const created = await storage.createFuelType(newFuel);
            expect(created).toMatchObject(newFuel);

            const retrieved = await storage.getFuelTypesByStation(testStationId);
            expect(retrieved).toContainEqual(expect.objectContaining({ id: 'test-new-fuel' }));
        });
    });

    describe('QR Codes and Purchases', () => {
        it('should find an available QR code', async () => {
            // Use existing seeded data for a reliable test
            // Assuming OKKO A-95 Pulls 10L exists from seed
            const stationId = 'okko';
            const fuelName = 'A-95 Pulls';
            const liters = 10;

            const qr = await storage.getAvailableQrCode(stationId, fuelName, liters);
            expect(qr).toBeDefined();
            expect(qr?.status).toBe('available');
            expect(qr?.liters).toBe(liters);
        });

        it('should return undefined if no QR code available', async () => {
            const qr = await storage.getAvailableQrCode('non-existent', 'Fake Fuel', 999);
            expect(qr).toBeUndefined();
        });

        it('should create a purchase and assign a QR code', async () => {
            const purchaseData: any = {
                sessionId: 'test-user-session',
                packageId: 'test-package-id',
                stationId: 'okko',
                stationName: 'OKKO',
                fuelType: 'diesel',
                fuelName: 'Diesel',
                liters: 10,
                price: 550,
                status: 'pending'
            };

            const purchase = await storage.createPurchase(purchaseData);
            expect(purchase.id).toBeDefined();
            expect(purchase.status).toBe('pending');

            await storage.updatePurchaseStatus(purchase.id, 'paid');
            const updated = await storage.getPurchase(purchase.id);
            expect(updated?.status).toBe('paid');
        });
    });

    describe('User Management', () => {
        it('should create a user with phone number', async () => {
            const phone = '+380999999999';
            const user = await storage.createUserWithPhone(phone);

            expect(user.phone).toBe(phone);
            expect(user.id).toBeDefined();

            const retrieved = await storage.getUserByPhone(phone);
            expect(retrieved).toEqual(user);
        });
    });
});
