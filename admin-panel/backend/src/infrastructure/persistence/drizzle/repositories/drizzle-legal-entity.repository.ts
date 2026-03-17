import { eq } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { companies, contracts, userContracts, users, Company, Contract, UserContract, InsertCompany, InsertUserContract, User } from '../../../../shared/database/schema';
import { ILegalEntityRepository } from '../../../../domain/repositories/legal-entity.repository';

export class DrizzleLegalEntityRepository implements ILegalEntityRepository {
    async getCompanyByUserId(userId: string): Promise<Company | null> {
        const rows = await db
            .select()
            .from(companies)
            .where(eq(companies.userId, userId))
            .limit(1);
        
        return rows[0] || null;
    }

    async upsertCompany(data: InsertCompany & { userId: string }): Promise<Company> {
        const existing = await this.getCompanyByUserId(data.userId);

        if (existing) {
            const rows = await db
                .update(companies)
                .set({
                    ...data,
                    updatedAt: new Date()
                })
                .where(eq(companies.id, existing.id))
                .returning();
            return rows[0];
        } else {
            const rows = await db
                .insert(companies)
                .values(data)
                .returning();
            return rows[0];
        }
    }

    async getAvailableContracts(): Promise<Contract[]> {
        return db
            .select()
            .from(contracts)
            .where(eq(contracts.status, 'ACTIVE'));
    }

    async getAllContracts(): Promise<Contract[]> {
        return db
            .select()
            .from(contracts);
    }

    async getContractById(id: string): Promise<Contract | null> {
        const rows = await db
            .select()
            .from(contracts)
            .where(eq(contracts.id, id))
            .limit(1);
        
        return rows[0] || null;
    }

    async getUserContracts(userId: string): Promise<(UserContract & { contract: Contract })[]> {
        const rows = await db
            .select({
                userContract: userContracts,
                contract: contracts
            })
            .from(userContracts)
            .innerJoin(contracts, eq(userContracts.contractId, contracts.id))
            .where(eq(userContracts.userId, userId));
        
        return rows.map((r: { userContract: UserContract, contract: Contract }) => ({
            ...r.userContract,
            contract: r.contract
        }));
    }

    async getAllSignedContracts(): Promise<(UserContract & { contract: Contract, user: User, company: Company })[]> {
        const rows = await db
            .select({
                userContract: userContracts,
                contract: contracts,
                user: users,
                company: companies
            })
            .from(userContracts)
            .innerJoin(contracts, eq(userContracts.contractId, contracts.id))
            .innerJoin(users, eq(userContracts.userId, users.id))
            .innerJoin(companies, eq(userContracts.companyId, companies.id));
        
        return rows.map((r: { userContract: UserContract, contract: Contract, user: User, company: Company }) => ({
            ...r.userContract,
            contract: r.contract,
            user: r.user,
            company: r.company
        }));
    }

    async saveSignedContract(data: InsertUserContract): Promise<UserContract> {
        const rows = await db
            .insert(userContracts)
            .values(data)
            .returning();
        
        return rows[0];
    }
}
