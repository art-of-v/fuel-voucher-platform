namespace FuelFlow.Features.Referral.RedeemReferralCode;

public sealed record RedeemReferralCodeCommand(string UserId, string Code);
