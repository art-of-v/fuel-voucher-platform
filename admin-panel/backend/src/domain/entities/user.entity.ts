/**
 * User Entity
 * 
 * Domain representation of a user with validation and business logic.
 */

export interface UserProps {
    id: string;
    email: string | null;
    phone: string | null;
    firstName: string | null;
    lastName: string | null;
    birthdate: string | null;
    profileImageUrl: string | null;
    referralCode: string | null;
    referredBy: string | null;
    bonusBalance: number;
    createdAt: Date;
    updatedAt: Date;
}

export class UserEntity {
    private constructor(private readonly props: UserProps) { }

    static create(props: UserProps): UserEntity {
        return new UserEntity(props);
    }

    get id(): string {
        return this.props.id;
    }

    get email(): string | null {
        return this.props.email;
    }

    get phone(): string | null {
        return this.props.phone;
    }

    get firstName(): string | null {
        return this.props.firstName;
    }

    get lastName(): string | null {
        return this.props.lastName;
    }

    get fullName(): string {
        const parts = [this.props.firstName, this.props.lastName].filter(Boolean);
        return parts.join(' ') || 'Unknown User';
    }

    get birthdate(): string | null {
        return this.props.birthdate;
    }

    get profileImageUrl(): string | null {
        return this.props.profileImageUrl;
    }



    get referralCode(): string | null {
        return this.props.referralCode;
    }

    get referredBy(): string | null {
        return this.props.referredBy;
    }

    get bonusBalance(): number {
        return this.props.bonusBalance;
    }

    get createdAt(): Date {
        return this.props.createdAt;
    }

    get updatedAt(): Date {
        return this.props.updatedAt;
    }

    hasPhone(): boolean {
        return this.props.phone !== null;
    }

    hasEmail(): boolean {
        return this.props.email !== null;
    }



    hasReferralCode(): boolean {
        return this.props.referralCode !== null;
    }

    wasReferred(): boolean {
        return this.props.referredBy !== null;
    }

    canRedeemReferralCode(referrerUserId: string): boolean {
        // Cannot redeem own code
        if (this.props.id === referrerUserId) {
            return false;
        }
        // Cannot redeem if already referred
        if (this.props.referredBy !== null) {
            return false;
        }
        return true;
    }

    addBonus(amount: number): number {
        if (amount < 0) {
            throw new Error('Bonus amount must be positive');
        }
        return this.props.bonusBalance + amount;
    }

    toPlainObject(): UserProps {
        return { ...this.props };
    }
}
