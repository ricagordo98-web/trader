using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trader.Core.Models;

namespace Trader.Infrastructure.Persistence;

public static class InstrumentCatalog
{
    public const string SettingKey = "instruments.catalog";

    public const int CurrentVersion = 1;

    private static readonly (int Version, (string Symbol, string Name, AssetClass Class, string Currency)[] Items)[] Batches =
    [
        (1,
        [
            ("AVGO", "Broadcom", AssetClass.Stock, "USD"),
            ("ORCL", "Oracle", AssetClass.Stock, "USD"),
            ("CRM", "Salesforce", AssetClass.Stock, "USD"),
            ("ADBE", "Adobe", AssetClass.Stock, "USD"),
            ("INTC", "Intel", AssetClass.Stock, "USD"),
            ("CSCO", "Cisco", AssetClass.Stock, "USD"),
            ("QCOM", "Qualcomm", AssetClass.Stock, "USD"),
            ("TXN", "Texas Instruments", AssetClass.Stock, "USD"),
            ("IBM", "IBM", AssetClass.Stock, "USD"),
            ("PLTR", "Palantir", AssetClass.Stock, "USD"),
            ("UBER", "Uber", AssetClass.Stock, "USD"),
            ("PYPL", "PayPal", AssetClass.Stock, "USD"),
            ("ASML", "ASML", AssetClass.Stock, "USD"),
            ("TSM", "Taiwan Semiconductor", AssetClass.Stock, "USD"),
            ("SHOP", "Shopify", AssetClass.Stock, "USD"),
            ("COIN", "Coinbase", AssetClass.Stock, "USD"),
            ("BAC", "Bank of America", AssetClass.Stock, "USD"),
            ("GS", "Goldman Sachs", AssetClass.Stock, "USD"),
            ("MS", "Morgan Stanley", AssetClass.Stock, "USD"),
            ("V", "Visa", AssetClass.Stock, "USD"),
            ("MA", "Mastercard", AssetClass.Stock, "USD"),
            ("BRK-B", "Berkshire Hathaway", AssetClass.Stock, "USD"),
            ("JNJ", "Johnson & Johnson", AssetClass.Stock, "USD"),
            ("PFE", "Pfizer", AssetClass.Stock, "USD"),
            ("MRK", "Merck", AssetClass.Stock, "USD"),
            ("LLY", "Eli Lilly", AssetClass.Stock, "USD"),
            ("UNH", "UnitedHealth", AssetClass.Stock, "USD"),
            ("ABBV", "AbbVie", AssetClass.Stock, "USD"),
            ("NVO", "Novo Nordisk", AssetClass.Stock, "USD"),
            ("WMT", "Walmart", AssetClass.Stock, "USD"),
            ("COST", "Costco", AssetClass.Stock, "USD"),
            ("HD", "Home Depot", AssetClass.Stock, "USD"),
            ("MCD", "McDonald's", AssetClass.Stock, "USD"),
            ("KO", "Coca-Cola", AssetClass.Stock, "USD"),
            ("PEP", "PepsiCo", AssetClass.Stock, "USD"),
            ("NKE", "Nike", AssetClass.Stock, "USD"),
            ("SBUX", "Starbucks", AssetClass.Stock, "USD"),
            ("DIS", "Disney", AssetClass.Stock, "USD"),
            ("PG", "Procter & Gamble", AssetClass.Stock, "USD"),
            ("XOM", "Exxon Mobil", AssetClass.Stock, "USD"),
            ("CVX", "Chevron", AssetClass.Stock, "USD"),
            ("BA", "Boeing", AssetClass.Stock, "USD"),
            ("CAT", "Caterpillar", AssetClass.Stock, "USD"),
            ("GE", "GE Aerospace", AssetClass.Stock, "USD"),
            ("LMT", "Lockheed Martin", AssetClass.Stock, "USD"),
            ("VZ", "Verizon", AssetClass.Stock, "USD"),
            ("EDP.LS", "EDP", AssetClass.Stock, "EUR"),
            ("GALP.LS", "Galp Energia", AssetClass.Stock, "EUR"),
            ("JMT.LS", "Jerónimo Martins", AssetClass.Stock, "EUR"),
            ("BCP.LS", "Millennium BCP", AssetClass.Stock, "EUR"),
            ("SAP.DE", "SAP", AssetClass.Stock, "EUR"),
            ("SIE.DE", "Siemens", AssetClass.Stock, "EUR"),
            ("ASML.AS", "ASML (Amesterdão)", AssetClass.Stock, "EUR"),
            ("MC.PA", "LVMH", AssetClass.Stock, "EUR"),
            ("AIR.PA", "Airbus", AssetClass.Stock, "EUR"),
            ("SAN.MC", "Banco Santander", AssetClass.Stock, "EUR"),
            ("ITX.MC", "Inditex", AssetClass.Stock, "EUR"),
            ("TTE.PA", "TotalEnergies", AssetClass.Stock, "EUR"),
            ("VTI", "Vanguard Total Stock Market", AssetClass.Etf, "USD"),
            ("VEA", "Vanguard Developed Markets", AssetClass.Etf, "USD"),
            ("VWO", "Vanguard Emerging Markets", AssetClass.Etf, "USD"),
            ("EFA", "iShares MSCI EAFE", AssetClass.Etf, "USD"),
            ("EEM", "iShares MSCI Emerging Markets", AssetClass.Etf, "USD"),
            ("DIA", "SPDR Dow Jones", AssetClass.Etf, "USD"),
            ("SCHD", "Schwab US Dividend", AssetClass.Etf, "USD"),
            ("VUG", "Vanguard Growth", AssetClass.Etf, "USD"),
            ("XLF", "Financial Select Sector", AssetClass.Etf, "USD"),
            ("XLE", "Energy Select Sector", AssetClass.Etf, "USD"),
            ("XLV", "Health Care Select Sector", AssetClass.Etf, "USD"),
            ("XLY", "Consumer Discretionary Select", AssetClass.Etf, "USD"),
            ("XLP", "Consumer Staples Select", AssetClass.Etf, "USD"),
            ("XLI", "Industrial Select Sector", AssetClass.Etf, "USD"),
            ("XLU", "Utilities Select Sector", AssetClass.Etf, "USD"),
            ("SMH", "VanEck Semiconductor", AssetClass.Etf, "USD"),
            ("SLV", "iShares Silver Trust", AssetClass.Etf, "USD"),
            ("USO", "United States Oil Fund", AssetClass.Etf, "USD"),
            ("HYG", "iShares High Yield Corporate Bond", AssetClass.Etf, "USD"),
            ("LQD", "iShares Investment Grade Corporate Bond", AssetClass.Etf, "USD"),
            ("AGG", "iShares Core US Aggregate Bond", AssetClass.Etf, "USD"),
            ("VNQ", "Vanguard Real Estate", AssetClass.Etf, "USD"),
            ("IBIT", "iShares Bitcoin Trust", AssetClass.Etf, "USD"),
            ("VWCE.DE", "Vanguard FTSE All-World (EUR)", AssetClass.Etf, "EUR"),
            ("EUNL.DE", "iShares Core MSCI World (EUR)", AssetClass.Etf, "EUR"),
            ("SXR8.DE", "iShares Core S&P 500 (EUR)", AssetClass.Etf, "EUR"),
            ("IWDA.AS", "iShares Core MSCI World (Amesterdão)", AssetClass.Etf, "EUR"),
            ("EXS1.DE", "iShares Core DAX", AssetClass.Etf, "EUR"),
            ("PL=F", "Platinum", AssetClass.Commodity, "USD"),
            ("PA=F", "Palladium", AssetClass.Commodity, "USD"),
            ("ZC=F", "Corn", AssetClass.Commodity, "USc"),
            ("ZW=F", "Wheat", AssetClass.Commodity, "USc"),
            ("ZS=F", "Soybeans", AssetClass.Commodity, "USc"),
            ("KC=F", "Coffee", AssetClass.Commodity, "USc"),
            ("SB=F", "Sugar", AssetClass.Commodity, "USc"),
            ("CC=F", "Cocoa", AssetClass.Commodity, "USD"),
            ("CT=F", "Cotton", AssetClass.Commodity, "USc"),
            ("HO=F", "Heating Oil", AssetClass.Commodity, "USD"),
            ("RB=F", "Gasoline RBOB", AssetClass.Commodity, "USD"),
            ("LE=F", "Live Cattle", AssetClass.Commodity, "USc"),
            ("ZL=F", "Soybean Oil", AssetClass.Commodity, "USc"),
            ("OJ=F", "Orange Juice", AssetClass.Commodity, "USc"),
            ("^GSPC", "S&P 500", AssetClass.Index, "USD"),
            ("^IXIC", "Nasdaq Composite", AssetClass.Index, "USD"),
            ("^NDX", "Nasdaq 100", AssetClass.Index, "USD"),
            ("^DJI", "Dow Jones", AssetClass.Index, "USD"),
            ("^RUT", "Russell 2000", AssetClass.Index, "USD"),
            ("^VIX", "VIX (volatilidade)", AssetClass.Index, "USD"),
            ("^FTSE", "FTSE 100", AssetClass.Index, "GBP"),
            ("^GDAXI", "DAX", AssetClass.Index, "EUR"),
            ("^FCHI", "CAC 40", AssetClass.Index, "EUR"),
            ("^STOXX50E", "Euro Stoxx 50", AssetClass.Index, "EUR"),
            ("^IBEX", "IBEX 35", AssetClass.Index, "EUR"),
            ("PSI20.LS", "PSI", AssetClass.Index, "EUR"),
            ("^N225", "Nikkei 225", AssetClass.Index, "JPY"),
            ("^HSI", "Hang Seng", AssetClass.Index, "HKD"),
            ("^TNX", "Yield obrigações EUA 10 anos", AssetClass.Index, "USD"),
            ("DX-Y.NYB", "Índice do dólar (DXY)", AssetClass.Index, "USD"),
            ("NZDUSD=X", "NZD/USD", AssetClass.Forex, "USD"),
            ("EURJPY=X", "EUR/JPY", AssetClass.Forex, "JPY"),
            ("GBPJPY=X", "GBP/JPY", AssetClass.Forex, "JPY"),
            ("EURCHF=X", "EUR/CHF", AssetClass.Forex, "CHF"),
            ("AUDJPY=X", "AUD/JPY", AssetClass.Forex, "JPY"),
            ("EURAUD=X", "EUR/AUD", AssetClass.Forex, "AUD"),
            ("EURCAD=X", "EUR/CAD", AssetClass.Forex, "CAD"),
            ("GBPCHF=X", "GBP/CHF", AssetClass.Forex, "CHF"),
            ("USDMXN=X", "USD/MXN", AssetClass.Forex, "MXN"),
            ("USDZAR=X", "USD/ZAR", AssetClass.Forex, "ZAR"),
            ("USDSEK=X", "USD/SEK", AssetClass.Forex, "SEK"),
            ("USDNOK=X", "USD/NOK", AssetClass.Forex, "NOK"),
            ("BTC-USD", "Bitcoin", AssetClass.Crypto, "USD"),
            ("ETH-USD", "Ethereum", AssetClass.Crypto, "USD"),
            ("BNB-USD", "BNB", AssetClass.Crypto, "USD"),
            ("SOL-USD", "Solana", AssetClass.Crypto, "USD"),
            ("XRP-USD", "XRP", AssetClass.Crypto, "USD"),
            ("ADA-USD", "Cardano", AssetClass.Crypto, "USD"),
            ("DOGE-USD", "Dogecoin", AssetClass.Crypto, "USD"),
            ("AVAX-USD", "Avalanche", AssetClass.Crypto, "USD"),
            ("DOT-USD", "Polkadot", AssetClass.Crypto, "USD"),
            ("LINK-USD", "Chainlink", AssetClass.Crypto, "USD"),
            ("LTC-USD", "Litecoin", AssetClass.Crypto, "USD"),
            ("BCH-USD", "Bitcoin Cash", AssetClass.Crypto, "USD"),
            ("TRX-USD", "TRON", AssetClass.Crypto, "USD"),
            ("XLM-USD", "Stellar", AssetClass.Crypto, "USD"),
            ("ATOM-USD", "Cosmos", AssetClass.Crypto, "USD"),
            ("NEAR-USD", "NEAR Protocol", AssetClass.Crypto, "USD"),
            ("SHIB-USD", "Shiba Inu", AssetClass.Crypto, "USD"),
            ("UNI7083-USD", "Uniswap", AssetClass.Crypto, "USD"),
            ("AAVE-USD", "Aave", AssetClass.Crypto, "USD")
        ])
    ];

    public static IEnumerable<(string Symbol, string Name, AssetClass Class, string Currency)> AllItems() =>
        Batches.SelectMany(b => b.Items);

    public static async Task<int> ApplyAsync(TraderDbContext dbContext, ILogger logger, CancellationToken cancellationToken)
    {
        AppSetting? setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);
        int applied = setting is not null && int.TryParse(setting.Value, out int version) ? version : 0;

        if (applied >= CurrentVersion)
        {
            return 0;
        }

        HashSet<string> existing = (await dbContext.Instruments.Select(i => i.Symbol).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int nextOrder = (await dbContext.Instruments.Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        int added = 0;

        foreach ((string symbol, string name, AssetClass assetClass, string currency) in Batches.Where(b => b.Version > applied).SelectMany(b => b.Items))
        {
            if (!existing.Add(symbol))
            {
                continue;
            }

            dbContext.Instruments.Add(new Instrument { Symbol = symbol, Name = name, AssetClass = assetClass, Currency = currency, SortOrder = nextOrder++ });
            added++;
        }

        if (setting is null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = SettingKey, Value = CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        else
        {
            setting.Value = CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Instrument catalog v{Version}: added {Count} instruments", CurrentVersion, added);
        return added;
    }
}
