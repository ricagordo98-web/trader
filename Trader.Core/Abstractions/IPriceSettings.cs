namespace Trader.Core.Abstractions;

public static class CryptoBasis
{
    public const string Usd = "usd";
    public const string Exchange = "exchange";

    public static bool IsValid(string value) => value is Usd or Exchange;
}

public sealed record PriceSourceSettings(string CryptoBasis);

public interface IPriceSettings
{
    Task<string> GetCryptoBasisAsync(CancellationToken cancellationToken);

    Task<string?> SetCryptoBasisAsync(string basis, CancellationToken cancellationToken);
}
