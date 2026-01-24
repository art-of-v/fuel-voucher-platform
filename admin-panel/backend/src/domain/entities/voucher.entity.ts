/**
 * Voucher Entity
 * 
 * Domain representation of a voucher with business logic.
 */

export type VoucherStatus =
    | 'imported'
    | 'available'
    | 'reserved'
    | 'sold'
    | 'used'
    | 'expired';

export interface VoucherProps {
    id: string;
    provider: string;
    externalId: string | null;
    fuelType: string;
    fuelSubtype: string | null;
    amount: number;
    unit: string;
    expirationDate: Date | null;
    status: VoucherStatus;
    redemptionRules: string | null;
    imageUrl: string | null;
    qrCodeData: string | null;
    originalFileName: string | null;
    source: string;
    importJobId: string | null;
    assignedToUserId: string | null;
    purchaseId: number | null;
    createdAt: Date;
    updatedAt: Date;
}

export class VoucherEntity {
    private constructor(private readonly props: VoucherProps) { }

    static create(props: VoucherProps): VoucherEntity {
        return new VoucherEntity(props);
    }

    get id(): string {
        return this.props.id;
    }

    get provider(): string {
        return this.props.provider;
    }

    get externalId(): string | null {
        return this.props.externalId;
    }

    get fuelType(): string {
        return this.props.fuelType;
    }

    get fuelSubtype(): string | null {
        return this.props.fuelSubtype;
    }

    get amount(): number {
        return this.props.amount;
    }

    get unit(): string {
        return this.props.unit;
    }

    get expirationDate(): Date | null {
        return this.props.expirationDate;
    }

    get status(): VoucherStatus {
        return this.props.status;
    }

    get redemptionRules(): string | null {
        return this.props.redemptionRules;
    }

    get imageUrl(): string | null {
        return this.props.imageUrl;
    }

    get qrCodeData(): string | null {
        return this.props.qrCodeData;
    }

    get originalFileName(): string | null {
        return this.props.originalFileName;
    }

    get source(): string {
        return this.props.source;
    }

    get importJobId(): string | null {
        return this.props.importJobId;
    }

    get assignedToUserId(): string | null {
        return this.props.assignedToUserId;
    }

    get purchaseId(): number | null {
        return this.props.purchaseId;
    }

    get createdAt(): Date {
        return this.props.createdAt;
    }

    get updatedAt(): Date {
        return this.props.updatedAt;
    }

    isAvailable(): boolean {
        return this.props.status === 'available' || this.props.status === 'imported';
    }

    isAssigned(): boolean {
        return this.props.assignedToUserId !== null;
    }

    isSold(): boolean {
        return this.props.status === 'sold';
    }

    isExpired(): boolean {
        if (!this.props.expirationDate) {
            return false;
        }
        return new Date() > this.props.expirationDate;
    }

    canBeAssigned(): boolean {
        return this.isAvailable() && !this.isAssigned() && !this.isExpired();
    }

    matchesCriteria(provider: string, fuelType: string, liters: number): boolean {
        return (
            this.props.provider.toLowerCase() === provider.toLowerCase() &&
            this.props.fuelType.toLowerCase() === fuelType.toLowerCase() &&
            this.props.amount === liters
        );
    }

    getDisplayName(): string {
        return `${this.props.provider} ${this.props.fuelType} ${this.props.amount}${this.props.unit}`;
    }

    toPlainObject(): VoucherProps {
        return { ...this.props };
    }
}
