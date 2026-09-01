namespace FuelFlow.SharedKernel.Security;

internal static class RedisConnectionParser
{
    internal static string Parse(string connection)
    {
        if (!connection.Contains("://"))
            return connection;

        var uri = new Uri(connection);
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 6379;
        var password = uri.UserInfo?.Contains(':') == true
            ? uri.UserInfo.Split(':', 2)[1]
            : uri.UserInfo ?? "";
        var ssl = uri.Scheme.StartsWith("rediss", StringComparison.OrdinalIgnoreCase);

        var parts = new List<string> { $"{host}:{port}" };
        if (!string.IsNullOrEmpty(password))
            parts.Add($"password={password}");
        if (ssl)
            parts.Add("ssl=True");

        return string.Join(",", parts);
    }
}