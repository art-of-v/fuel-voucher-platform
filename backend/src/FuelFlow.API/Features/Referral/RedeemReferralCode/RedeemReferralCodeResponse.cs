namespace FuelFlow.Features.Referral.RedeemReferralCode;

public sealed record RedeemReferralCodeResponse(Guid UserId, string RedeemedCode, Guid ReferrerId);
