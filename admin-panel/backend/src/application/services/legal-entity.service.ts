import { ILegalEntityRepository } from '../../domain/repositories/legal-entity.repository';
import { InsertCompany } from '../../shared/database/schema';
import { IUserRepository } from '../../domain/repositories/user.repository';

export class LegalEntityService {
    constructor(
        private readonly legalEntityRepository: ILegalEntityRepository,
        private readonly userRepository: IUserRepository
    ) {}

    async getProfile(userId: string) {
        const company = await this.legalEntityRepository.getCompanyByUserId(userId);
        return { company };
    }

    async updateProfile(userId: string, data: InsertCompany) {
        // Ensure user is marked as LEGAL_ENTITY
        const user = await this.userRepository.findById(userId);
        if (user && user.userType !== 'LEGAL_ENTITY') {
            await this.userRepository.update(userId, { userType: 'LEGAL_ENTITY' } as any);
        }

        return this.legalEntityRepository.upsertCompany({
            ...data,
            userId
        });
    }

    async getAvailableContracts() {
        return this.legalEntityRepository.getAvailableContracts();
    }

    async getAllContracts() {
        return this.legalEntityRepository.getAllContracts();
    }

    async signContracts(userId: string, contractIds: string[], signatureData: string) {
        const company = await this.legalEntityRepository.getCompanyByUserId(userId);
        if (!company) {
            throw new Error('Company profile must be filled before signing contracts');
        }

        const signedContracts = await Promise.all(
            contractIds.map(async (contractId) => {
                return this.legalEntityRepository.saveSignedContract({
                    userId,
                    contractId,
                    companyId: company.id,
                    signatureData,
                    status: 'SIGNED'
                });
            })
        );

        return signedContracts;
    }

    async getSignedContracts(userId: string) {
        return this.legalEntityRepository.getUserContracts(userId);
    }

    async getAllSignedContracts() {
        return this.legalEntityRepository.getAllSignedContracts();
    }
}
