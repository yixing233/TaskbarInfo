using System.Security.Cryptography;

namespace TaskbarInfo;

public sealed record TemperatureHelperRequest(string Token);

public sealed record TemperatureHelperResponse(
    double? CpuTemperatureCelsius,
    double? GpuTemperatureCelsius,
    double? DiskTemperatureCelsius);

public static class TemperatureHelperProtocol
{
    public static bool HasValidToken(string? expectedToken, string? suppliedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(suppliedToken)) return false;
        byte[] expected = Convert.FromBase64String(expectedToken);
        byte[] supplied = Convert.FromBase64String(suppliedToken);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
