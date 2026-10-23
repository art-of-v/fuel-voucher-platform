namespace FuelFlow.UnitTests.Shared;

/// <summary>
/// Public keys only - the matching private keys are deliberately absent, so a test cannot
/// accidentally "prove" that enrollment accepts a key its own process just generated. Fixed
/// signatures over these keys live next to the tests that use them.
/// </summary>
internal static class DeviceTestKeys
{
    public const string RsaPrimary = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA5UEVP/q0+Zua7uriZrRB
        yqrPlKd8YBf8RMwgi4PO3lziCNHkdIPfD47Jgrd/lVOVzO+gP8P6efM8ux7Rumxo
        ecYqOAEmwrIxChrZYSqVsm3L3kw6f4PhLWIGwxcLcNGwlP59wSbaq1iTRjC32byp
        xm0tDYOK6VB5YZ9GxmlnFu03bbDpWraEvl5TWllJRHJsX2naEDwMAX6NNqkkXt1O
        I8zviUeOaCS70v8d3mjyiqyQDJbgqSGqDAyrcdTNvoW4qYdWxctA1oUIog8QBrV+
        1N0jR8aJA9voIoMEO6KtiKb55WQ3SUM+mLMOaU5wUvJsHWB68JjChQamAcNgNIce
        3wIDAQAB
        -----END PUBLIC KEY-----
        """;

    public const string RsaSecondary = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA2Ukbq93Q+YkYStpUDhxr
        KPeIaoBN6oS76TqODvpzi6AaB+G0v6yKZj929x1oBGFfLooyHy2chr1K+x1f2Lwd
        L8ikPSqGR6y3/T8wtiC1KHv8uFhOTENn8dKy83dDsjwCg4AvbXrqXu9WBp6Ajd5m
        1Xb+804F5CnL4lLTV6sjzbbzVgWE+M3/A3z8bg1DwQ9JjTxlfhroKK6FPTNyR2dt
        QyRYdz7IPuL5NUqQTcsi7syysxNE/51WnlTemt/1EkIyOR0Uas/DQekhUGuKkr/A
        Nl9IuetdKg0+lOZjqaL3Ri9vg7GQ/6RJkEPKDTS6+/Ct7/CBcQEPKYS/7GfGWXJv
        fQIDAQAB
        -----END PUBLIC KEY-----
        """;

    public const string EcPrimary = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAECU88eQnJyagG2qI/59RoBMCvAG8U
        l0UQ9FvPpvLSES4hnm5eiv1Phi39agVwb1ocrjjld1R8+dZ+6dnNa3wJtg==
        -----END PUBLIC KEY-----
        """;
}
