/**
 * Dependency Injection Container
 * 
 * Centralizes the creation and wiring of all dependencies.
 * This is a simple factory pattern - for larger apps, consider using a DI library.
 */

// Domain Services
import { FuelMatcherService } from '../../domain/services/fuel-matcher.service';
import { config } from '../../config';

// Infrastructure - Repositories
import { DrizzleUserRepository } from '../persistence/drizzle/repositories/drizzle-user.repository';
import { DrizzleOrderRepository } from '../persistence/drizzle/repositories/drizzle-order.repository';
import { DrizzleVoucherRepository } from '../persistence/drizzle/repositories/drizzle-voucher.repository';
import { DrizzleOutboxRepository } from '../persistence/drizzle/repositories/drizzle-outbox.repository';
import { DrizzleFulfillmentRepository } from '../persistence/drizzle/repositories/drizzle-fulfillment.repository';
import { DrizzlePackageRepository } from '../persistence/drizzle/repositories/drizzle-package.repository';
import { DrizzleFuelTypeRepository } from '../persistence/drizzle/repositories/drizzle-fuel-type.repository';
import { DrizzleQrCodeRepository } from '../persistence/drizzle/repositories/drizzle-qr-code.repository';
import { DrizzleNotificationRepository } from '../persistence/drizzle/repositories/drizzle-notification.repository';
import { DrizzleImportJobRepository } from '../persistence/drizzle/repositories/drizzle-import-job.repository';
import { DrizzlePhoneVerificationRepository } from '../persistence/drizzle/repositories/drizzle-phone-verification.repository';
import { DrizzleDeviceRepository } from '../persistence/drizzle/repositories/drizzle-device.repository';
import { DrizzleDeviceSessionRepository } from '../persistence/drizzle/repositories/drizzle-device-session.repository';

// Legacy Repositories (for backward compatibility during migration)
import { usersRepository } from '../../features/users/users.repository';
import { verificationRepository } from '../../features/users/verification.repository';
import { purchasesRepository } from '../../features/purchases/purchases.repository';
import { notificationsRepository } from '../../features/notifications/notifications.repository';
import { stationsRepository } from '../../features/stations/stations.repository';
import { fuelTypesRepository } from '../../features/stations/fuel-types.repository';
import { packagesRepository } from '../../features/stations/packages.repository';
import { qrCodesRepository } from '../../features/inventory/qr-codes.repository';

// Infrastructure - SMS
import { sendVerificationCode } from '../../shared/infrastructure/twilio';

// Application Services
import { AuthService, IVerificationRepository, ISMSSender } from '../../application/services/auth.service';
import { PurchaseService, IPurchaseRepository, CreatePurchaseData, Purchase } from '../../application/services/purchase.service';
import { FulfillmentService } from '../../application/services/fulfillment.service';
import { UserService, INotificationRepository } from '../../application/services/user.service';
import { VoucherService } from '../../application/services/voucher.service';
import { MonobankService } from '../../services/monobank.service';

// Controllers
import { AuthController } from '../../presentation/http/controllers/auth.controller';
import { PurchaseController, CheckoutController } from '../../presentation/http/controllers/purchase.controller';
import { VoucherController, AdminVoucherController, TestVoucherController } from '../../presentation/http/controllers/voucher.controller';
import { UserController, AdminUserController } from '../../presentation/http/controllers/user.controller';
import { SyncController } from '../../presentation/http/controllers/sync.controller';
import { TestWebhookController } from '../../presentation/http/controllers/test-webhook.controller';
import { MonobankController } from '../../presentation/http/controllers/monobank.controller';

/**
 * Adapter: Wrap legacy verification repository
 * Maps legacy DB types (verified: number|null) to domain types (verified: boolean)
 */
const verificationRepositoryAdapter: IVerificationRepository = {
    async createPhoneVerification(phone: string, code: string) {
        const result = await verificationRepository.createPhoneVerification(phone, code);
        return {
            ...result,
            verified: result.verified === 1,
        };
    },
    async getLatestPhoneVerification(phone: string) {
        const result = await verificationRepository.getLatestPhoneVerification(phone);
        if (!result) return null;
        return {
            ...result,
            verified: result.verified === 1,
        };
    },
    async markPhoneVerified(id: number) {
        return verificationRepository.markPhoneVerified(id);
    },
};

/**
 * Adapter: Wrap Twilio SMS sender
 */
const smsSenderAdapter: ISMSSender = {
    async sendVerificationCode(phone: string, code: string) {
        return sendVerificationCode(phone, code);
    },
};

/**
 * Adapter: Wrap legacy purchase repository
 */
const purchaseRepositoryAdapter: IPurchaseRepository = {
    async createPurchase(data: CreatePurchaseData): Promise<Purchase> {
        return purchasesRepository.createPurchase(data) as Promise<Purchase>;
    },
    async getPurchase(id: number): Promise<Purchase | null> {
        return purchasesRepository.getPurchase(id) as Promise<Purchase | null>;
    },
    async getPurchaseByMonobankInvoice(invoiceId: string): Promise<Purchase | null> {
        return purchasesRepository.getPurchaseByMonobankInvoice(invoiceId) as Promise<Purchase | null>;
    },
    async getPurchasesByUserId(userId: string): Promise<Purchase[]> {
        return purchasesRepository.getPurchasesByUserId(userId) as Promise<Purchase[]>;
    },
    async updatePurchaseStatus(id: number, status: string, monobankInvoiceId?: string, monobankStatus?: string, qrCodeId?: number, voucherId?: string): Promise<void> {
        return purchasesRepository.updatePurchaseStatus(id, status, monobankInvoiceId, monobankStatus, qrCodeId, voucherId);
    },
    async getPurchaseWithQrCode(id: number): Promise<any> {
        return purchasesRepository.getPurchaseWithQrCode(id);
    },
    async getAllPurchases(): Promise<Purchase[]> {
        return purchasesRepository.getAllPurchases() as Promise<Purchase[]>;
    },
};

/**
 * Adapter: Wrap legacy notification repository
 */
const notificationRepositoryAdapter: INotificationRepository = {
    async createNotification(data: { userId: string; title: string; message: string }): Promise<void> {
        await notificationsRepository.createNotification(data);
    },
};

/**
 * Container class that creates and holds all dependencies
 */
export class Container {
    // Domain Services
    public readonly fuelMatcherService: FuelMatcherService;

    // Repositories (new architecture)
    public readonly userRepository: DrizzleUserRepository;
    public readonly orderRepository: DrizzleOrderRepository;
    public readonly voucherRepository: DrizzleVoucherRepository;
    public readonly outboxRepository: DrizzleOutboxRepository;
    public readonly fulfillmentRepository: DrizzleFulfillmentRepository;
    public readonly packageRepository: DrizzlePackageRepository;
    public readonly fuelTypeRepository: DrizzleFuelTypeRepository;
    public readonly qrCodeRepository: DrizzleQrCodeRepository;
    public readonly notificationRepository: DrizzleNotificationRepository;
    public readonly importJobRepository: DrizzleImportJobRepository;
    public readonly phoneVerificationRepository: DrizzlePhoneVerificationRepository;
    public readonly deviceRepository: DrizzleDeviceRepository;
    public readonly deviceSessionRepository: DrizzleDeviceSessionRepository;

    // Application Services
    public readonly authService: AuthService;
    public readonly purchaseService: PurchaseService;
    public readonly fulfillmentService: FulfillmentService;
    public readonly userService: UserService;
    public readonly voucherService: VoucherService;
    public readonly monobankService: MonobankService;

    // Controllers
    public readonly authController: AuthController;
    public readonly purchaseController: PurchaseController;
    public readonly checkoutController: CheckoutController;
    public readonly voucherController: VoucherController;
    public readonly adminVoucherController: AdminVoucherController;
    public readonly testVoucherController: TestVoucherController;
    public readonly userController: UserController;
    public readonly adminUserController: AdminUserController;
    public readonly syncController: SyncController;
    public readonly testWebhookController: TestWebhookController;
    public readonly monobankController: MonobankController;

    // Legacy repositories (exposed for backward compatibility)
    public readonly legacyRepositories = {
        users: usersRepository,
        verification: verificationRepository,
        purchases: purchasesRepository,
        notifications: notificationsRepository,
        stations: stationsRepository,
        fuelTypes: fuelTypesRepository,
        packages: packagesRepository,
        qrCodes: qrCodesRepository,
    };

    constructor() {
        // Initialize domain services
        this.fuelMatcherService = new FuelMatcherService();

        // Initialize repositories
        this.userRepository = new DrizzleUserRepository();
        this.orderRepository = new DrizzleOrderRepository();
        this.voucherRepository = new DrizzleVoucherRepository();
        this.outboxRepository = new DrizzleOutboxRepository();
        this.fulfillmentRepository = new DrizzleFulfillmentRepository();
        this.packageRepository = new DrizzlePackageRepository();
        this.fuelTypeRepository = new DrizzleFuelTypeRepository();
        this.qrCodeRepository = new DrizzleQrCodeRepository();
        this.notificationRepository = new DrizzleNotificationRepository();
        this.importJobRepository = new DrizzleImportJobRepository();
        this.phoneVerificationRepository = new DrizzlePhoneVerificationRepository();
        this.deviceRepository = new DrizzleDeviceRepository();
        this.deviceSessionRepository = new DrizzleDeviceSessionRepository();

        // Initialize application services
        this.authService = new AuthService(
            this.userRepository,
            verificationRepositoryAdapter,
            smsSenderAdapter,
            this.deviceRepository,
            this.deviceSessionRepository
        );

        this.purchaseService = new PurchaseService(
            purchaseRepositoryAdapter,
            this.orderRepository,
            this.voucherRepository
        );

        this.fulfillmentService = new FulfillmentService(
            this.orderRepository
        );

        this.userService = new UserService(
            this.userRepository,
            notificationRepositoryAdapter
        );

        this.voucherService = new VoucherService(
            this.voucherRepository
        );

        this.monobankService = new MonobankService(
            config.monobank.apiToken
        );

        // Initialize controllers
        this.authController = new AuthController(this.authService);
        this.purchaseController = new PurchaseController(this.purchaseService);
        this.checkoutController = new CheckoutController(this.purchaseService);
        this.voucherController = new VoucherController(this.voucherService);
        this.adminVoucherController = new AdminVoucherController(this.voucherService);
        this.testVoucherController = new TestVoucherController(this.voucherService);
        this.userController = new UserController(this.userService);
        this.adminUserController = new AdminUserController(this.userService);
        this.syncController = new SyncController(this.orderRepository, this.voucherRepository);
        this.testWebhookController = new TestWebhookController(this.voucherRepository, this.orderRepository);
        this.monobankController = new MonobankController(
            this.monobankService,
            this.purchaseService,
            config.monobank.webhookUrl,
            config.app.frontendUrl
        );
    }
}

// Singleton container instance
let containerInstance: Container | null = null;

/**
 * Get the container instance (creates if not exists)
 */
export function getContainer(): Container {
    if (!containerInstance) {
        containerInstance = new Container();
    }
    return containerInstance;
}

/**
 * Reset container (for testing)
 */
export function resetContainer(): void {
    containerInstance = null;
}
