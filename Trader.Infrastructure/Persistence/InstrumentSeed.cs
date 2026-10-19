using Trader.Core.Models;

namespace Trader.Infrastructure.Persistence;

internal static class InstrumentSeed
{
    public static readonly Instrument[] All = Build();

    private static Instrument[] Build()
    {
        (string Symbol, string Name, AssetClass Class, string Currency)[] items =
        [
            ("AAPL", "Apple", AssetClass.Stock, "USD"),
            ("MSFT", "Microsoft", AssetClass.Stock, "USD"),
            ("NVDA", "NVIDIA", AssetClass.Stock, "USD"),
            ("TSLA", "Tesla", AssetClass.Stock, "USD"),
            ("AMZN", "Amazon", AssetClass.Stock, "USD"),
            ("GOOGL", "Alphabet", AssetClass.Stock, "USD"),
            ("META", "Meta Platforms", AssetClass.Stock, "USD"),
            ("AMD", "Advanced Micro Devices", AssetClass.Stock, "USD"),
            ("NFLX", "Netflix", AssetClass.Stock, "USD"),
            ("JPM", "JPMorgan Chase", AssetClass.Stock, "USD"),
            ("GC=F", "Gold", AssetClass.Commodity, "USD"),
            ("SI=F", "Silver", AssetClass.Commodity, "USD"),
            ("CL=F", "Crude Oil WTI", AssetClass.Commodity, "USD"),
            ("BZ=F", "Brent Crude", AssetClass.Commodity, "USD"),
            ("NG=F", "Natural Gas", AssetClass.Commodity, "USD"),
            ("HG=F", "Copper", AssetClass.Commodity, "USD"),
            ("SPY", "SPDR S&P 500", AssetClass.Etf, "USD"),
            ("QQQ", "Invesco QQQ", AssetClass.Etf, "USD"),
            ("VOO", "Vanguard S&P 500", AssetClass.Etf, "USD"),
            ("IWM", "iShares Russell 2000", AssetClass.Etf, "USD"),
            ("GLD", "SPDR Gold Shares", AssetClass.Etf, "USD"),
            ("TLT", "iShares 20+ Year Treasury", AssetClass.Etf, "USD"),
            ("XLK", "Technology Select Sector", AssetClass.Etf, "USD"),
            ("ARKK", "ARK Innovation", AssetClass.Etf, "USD"),
            ("EURUSD=X", "EUR/USD", AssetClass.Forex, "USD"),
            ("GBPUSD=X", "GBP/USD", AssetClass.Forex, "USD"),
            ("USDJPY=X", "USD/JPY", AssetClass.Forex, "JPY"),
            ("USDCHF=X", "USD/CHF", AssetClass.Forex, "CHF"),
            ("AUDUSD=X", "AUD/USD", AssetClass.Forex, "USD"),
            ("USDCAD=X", "USD/CAD", AssetClass.Forex, "CAD"),
            ("EURGBP=X", "EUR/GBP", AssetClass.Forex, "GBP")
        ];

        return items
            .Select((item, index) => new Instrument
            {
                Id = index + 1,
                Symbol = item.Symbol,
                Name = item.Name,
                AssetClass = item.Class,
                Currency = item.Currency,
                SortOrder = index
            })
            .ToArray();
    }
}
