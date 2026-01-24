/**
 * Admin Controller
 * 
 * Handles admin-related HTTP endpoints for managing stations,
 * packages, fuel types, and QR codes.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { ZodError } from 'zod';
import { logger } from '../../../infrastructure/logging/logger';

// Legacy repositories (until we create proper services)
import { stationsRepository } from '../../../features/stations/stations.repository';
import { packagesRepository } from '../../../features/stations/packages.repository';
import { fuelTypesRepository } from '../../../features/stations/fuel-types.repository';
import { qrCodesRepository } from '../../../features/inventory/qr-codes.repository';
import { insertStationSchema, insertFuelPackageSchema, insertFuelTypeSchema, insertQrCodeSchema } from '../../../shared/database/schema';

const log = logger.child({ component: 'AdminController' });

/**
 * Admin Station Controller
 */
export class AdminStationController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/', this.getAll.bind(this));
        this.router.get('/:id', this.getById.bind(this));
        this.router.post('/', this.create.bind(this));
        this.router.put('/:id', this.update.bind(this));
        this.router.delete('/:id', this.delete.bind(this));
    }

    private async getAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const list = await stationsRepository.getAllStations();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching stations');
            next(error);
        }
    }

    private async getById(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const station = await stationsRepository.getStation(req.params.id);
            if (!station) {
                res.status(404).json({ error: 'Station not found' });
                return;
            }
            res.json(station);
        } catch (error) {
            log.error({ err: error }, 'Error fetching station');
            next(error);
        }
    }

    private async create(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const payload = insertStationSchema.parse(req.body);
            const record = await stationsRepository.createStation(payload);
            res.status(201).json(record);
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: 'Invalid station data', details: error.errors });
                return;
            }
            log.error({ err: error }, 'Error creating station');
            next(error);
        }
    }

    private async update(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const record = await stationsRepository.updateStation(req.params.id, req.body);
            res.json(record);
        } catch (error) {
            log.error({ err: error }, 'Error updating station');
            next(error);
        }
    }

    private async delete(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            await stationsRepository.deleteStation(req.params.id);
            res.json({ success: true });
        } catch (error) {
            log.error({ err: error }, 'Error deleting station');
            next(error);
        }
    }
}

/**
 * Admin Package Controller
 */
export class AdminPackageController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/suggestions', this.getSuggestions.bind(this));
        this.router.get('/', this.getAll.bind(this));
        this.router.get('/station/:stationId', this.getByStation.bind(this));
        this.router.post('/', this.create.bind(this));
        this.router.put('/:id', this.update.bind(this));
        this.router.delete('/:id', this.delete.bind(this));
    }

    private async getAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const list = await packagesRepository.getAllPackages();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching packages');
            next(error);
        }
    }

    private async getByStation(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const packages = await packagesRepository.getPackagesByStation(req.params.stationId);
            res.json(packages);
        } catch (error) {
            log.error({ err: error }, 'Error fetching packages by station');
            next(error);
        }
    }

    private async getSuggestions(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const suggestions = await packagesRepository.getPackageSuggestions();
            res.json(suggestions);
        } catch (error) {
            log.error({ err: error }, 'Error fetching package suggestions');
            next(error);
        }
    }

    private async create(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const payload = insertFuelPackageSchema.parse(req.body);
            const record = await packagesRepository.createPackage(payload);
            res.status(201).json(record);
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: 'Invalid package data', details: error.errors });
                return;
            }
            log.error({ err: error }, 'Error creating package');
            next(error);
        }
    }

    private async update(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const record = await packagesRepository.updatePackage(req.params.id, req.body);
            res.json(record);
        } catch (error) {
            log.error({ err: error }, 'Error updating package');
            next(error);
        }
    }

    private async delete(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            await packagesRepository.deletePackage(req.params.id);
            res.json({ success: true });
        } catch (error) {
            log.error({ err: error }, 'Error deleting package');
            next(error);
        }
    }
}

/**
 * Admin Fuel Type Controller
 */
export class AdminFuelTypeController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/', this.getAll.bind(this));
        this.router.get('/:id', this.getById.bind(this));
        this.router.post('/', this.create.bind(this));
        this.router.put('/:id', this.update.bind(this));
        this.router.delete('/:id', this.delete.bind(this));
    }

    private async getAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const list = await fuelTypesRepository.getAllFuelTypes();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching fuel types');
            next(error);
        }
    }

    private async getById(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const fuelType = await fuelTypesRepository.getFuelType(req.params.id);
            if (!fuelType) {
                res.status(404).json({ error: 'Fuel type not found' });
                return;
            }
            res.json(fuelType);
        } catch (error) {
            log.error({ err: error }, 'Error fetching fuel type');
            next(error);
        }
    }

    private async create(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const payload = insertFuelTypeSchema.parse(req.body);
            const record = await fuelTypesRepository.createFuelType(payload);
            res.status(201).json(record);
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: 'Invalid fuel type data', details: error.errors });
                return;
            }
            log.error({ err: error }, 'Error creating fuel type');
            next(error);
        }
    }

    private async update(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const record = await fuelTypesRepository.updateFuelType(req.params.id, req.body);
            res.json(record);
        } catch (error) {
            log.error({ err: error }, 'Error updating fuel type');
            next(error);
        }
    }

    private async delete(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            await fuelTypesRepository.deleteFuelType(req.params.id);
            res.json({ success: true });
        } catch (error) {
            log.error({ err: error }, 'Error deleting fuel type');
            next(error);
        }
    }
}

/**
 * Admin QR Code Controller
 */
export class AdminQrCodeController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/', this.getAll.bind(this));
        this.router.post('/', this.create.bind(this));
        this.router.delete('/:id', this.delete.bind(this));
    }

    private async getAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const list = await qrCodesRepository.getAllQrCodes();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching QR codes');
            next(error);
        }
    }

    private async create(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const payload = insertQrCodeSchema.parse(req.body);
            const record = await qrCodesRepository.createQrCode(payload);
            res.status(201).json(record);
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: 'Invalid QR code data', details: error.errors });
                return;
            }
            log.error({ err: error }, 'Error creating QR code');
            next(error);
        }
    }

    private async delete(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const id = parseInt(req.params.id);
            await qrCodesRepository.deleteQrCode(id);
            res.json({ success: true });
        } catch (error) {
            log.error({ err: error }, 'Error deleting QR code');
            next(error);
        }
    }
}

/**
 * Public Station Controller (for map display)
 */
export class PublicStationController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/', this.getAll.bind(this));
        this.router.get('/:id', this.getById.bind(this));
    }

    private async getAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const list = await stationsRepository.getAllStations();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching stations');
            next(error);
        }
    }

    private async getById(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const station = await stationsRepository.getStation(req.params.id);
            if (!station) {
                res.status(404).json({ error: 'Station not found' });
                return;
            }
            res.json(station);
        } catch (error) {
            log.error({ err: error }, 'Error fetching station');
            next(error);
        }
    }
}

/**
 * Public Package Controller (for mobile app)
 */
export class PublicPackageController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/', this.getAll.bind(this));
        this.router.get('/station/:stationId', this.getByStation.bind(this));
    }

    private async getAll(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const list = await packagesRepository.getAllPackages();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching packages');
            next(error);
        }
    }

    private async getByStation(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const packages = await packagesRepository.getPackagesByStation(req.params.stationId);
            res.json(packages);
        } catch (error) {
            log.error({ err: error }, 'Error fetching packages by station');
            next(error);
        }
    }
}
