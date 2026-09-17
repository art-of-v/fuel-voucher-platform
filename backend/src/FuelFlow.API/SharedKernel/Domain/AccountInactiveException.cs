namespace FuelFlow.SharedKernel.Domain;

public sealed class AccountInactiveException : Exception
{
    public const string Code = "account_inactive";

    public AccountInactiveException() : base("Account is not activated. Contact administrator to activate your account.")
    {
    }
}