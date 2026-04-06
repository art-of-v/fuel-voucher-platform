/**
 * Legal Entity Repository Interface
 * 
 * Defines operations for companies and contracts.
 */

import { Company, Contract, UserContract, InsertCompany, InsertUserContract, User, Station } from '../../shared/database/schema';

export interface ILegalEntityRepository {
    // Company operations
    getCompanyByUserId(userId: string): Promise<Company | null>;
    upsertCompany(data: InsertCompany & { userId: string }): Promise<Company>;

    // Contract operations
    getAvailableContracts(): Promise<Contract[]>;
    getAllContracts(): Promise<Contract[]>;
    getContractById(id: string): Promise<Contract | null>;
    
    // User Contract (Signed) operations
    getUserContracts(userId: string): Promise<(UserContract & { contract: Contract, station?: Station | null })[]>;
    getAllSignedContracts(): Promise<(UserContract & { contract: Contract, user: User, company: Company, station?: Station | null })[]>;
    saveSignedContract(data: InsertUserContract): Promise<UserContract>;
}
