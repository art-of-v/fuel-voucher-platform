import { Router, Request, Response, NextFunction } from 'express';
import { LegalEntityService } from '../../../application/services/legal-entity.service';
import { logger } from '../../../infrastructure/logging/logger';

const log = logger.child({ component: 'LegalEntityController' });

export class LegalEntityController {
    public readonly router: Router;

    constructor(private readonly legalEntityService: LegalEntityService) {
        this.router = Router();
        this.setupRoutes();
    }

    private setupRoutes(): void {
        this.router.get('/profile', this.getProfile.bind(this));
        this.router.post('/profile', this.updateProfile.bind(this));
        this.router.get('/contracts', this.getAvailableContracts.bind(this));
        this.router.post('/sign', this.signContracts.bind(this));
        this.router.get('/contracts/signed', this.getSignedContracts.bind(this));
    }

    private async getProfile(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).user?.id || req.body.userId; // Support both session and explicit ID for testing
            if (!userId) {
                res.status(401).json({ error: 'Unauthorized' });
                return;
            }
            const profile = await this.legalEntityService.getProfile(userId);
            res.json(profile);
        } catch (error) {
            next(error);
        }
    }

    private async updateProfile(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).user?.id || req.body.userId;
            if (!userId) {
                res.status(401).json({ error: 'Unauthorized' });
                return;
            }
            const company = await this.legalEntityService.updateProfile(userId, req.body);
            res.json(company);
        } catch (error) {
            next(error);
        }
    }

    private async getAvailableContracts(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const contracts = await this.legalEntityService.getAvailableContracts();
            res.json(contracts);
        } catch (error) {
            next(error);
        }
    }

    private async signContracts(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).user?.id || req.body.userId;
            const { contractIds, signatureData } = req.body;
            
            if (!userId) {
                res.status(401).json({ error: 'Unauthorized' });
                return;
            }

            if (!contractIds || !Array.isArray(contractIds) || !signatureData) {
                res.status(400).json({ error: 'contractIds (array) and signatureData are required' });
                return;
            }

            log.info({ userId, contractCount: contractIds.length }, 'Signing contracts');
            const signed = await this.legalEntityService.signContracts(userId, contractIds, signatureData);
            res.json(signed);
        } catch (error) {
            next(error);
        }
    }

    private async getSignedContracts(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).user?.id || req.body.userId;
            if (!userId) {
                res.status(401).json({ error: 'Unauthorized' });
                return;
            }
            const signed = await this.legalEntityService.getSignedContracts(userId);
            res.json(signed);
        } catch (error) {
            next(error);
        }
    }
}

/**
 * Admin Legal Entity Controller
 * 
 * Handles admin legal entity and contract management endpoints.
 */
export class AdminLegalEntityController {
    public readonly router: Router;

    constructor(private readonly legalEntityService: LegalEntityService) {
        this.router = Router();
        this.setupRoutes();
    }

    private setupRoutes(): void {
        this.router.get('/contracts', this.getAllContracts.bind(this));
        this.router.get('/signed-contracts', this.getAllSignedContracts.bind(this));
    }

    private async getAllContracts(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const contracts = await this.legalEntityService.getAllContracts();
            res.json(contracts);
        } catch (error) {
            next(error);
        }
    }

    private async getAllSignedContracts(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const signed = await this.legalEntityService.getAllSignedContracts();
            
            // Map to the format frontend expects
            const formatted = signed.map(s => ({
                id: s.id,
                userId: s.userId,
                userName: `${s.user.firstName} ${s.user.lastName}`,
                companyName: s.company.name,
                contractTitle: s.contract.title,
                signedAt: s.signedAt,
                signatureData: s.signatureData
            }));
            
            res.json(formatted);
        } catch (error) {
            next(error);
        }
    }
}
