using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Trader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AssetClass = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Instruments",
                columns: new[] { "Id", "AssetClass", "Currency", "Name", "SortOrder", "Symbol" },
                values: new object[,]
                {
                    { 1, "Stock", "USD", "Apple", 0, "AAPL" },
                    { 2, "Stock", "USD", "Microsoft", 1, "MSFT" },
                    { 3, "Stock", "USD", "NVIDIA", 2, "NVDA" },
                    { 4, "Stock", "USD", "Tesla", 3, "TSLA" },
                    { 5, "Stock", "USD", "Amazon", 4, "AMZN" },
                    { 6, "Stock", "USD", "Alphabet", 5, "GOOGL" },
                    { 7, "Stock", "USD", "Meta Platforms", 6, "META" },
                    { 8, "Stock", "USD", "Advanced Micro Devices", 7, "AMD" },
                    { 9, "Stock", "USD", "Netflix", 8, "NFLX" },
                    { 10, "Stock", "USD", "JPMorgan Chase", 9, "JPM" },
                    { 11, "Commodity", "USD", "Gold", 10, "GC=F" },
                    { 12, "Commodity", "USD", "Silver", 11, "SI=F" },
                    { 13, "Commodity", "USD", "Crude Oil WTI", 12, "CL=F" },
                    { 14, "Commodity", "USD", "Brent Crude", 13, "BZ=F" },
                    { 15, "Commodity", "USD", "Natural Gas", 14, "NG=F" },
                    { 16, "Commodity", "USD", "Copper", 15, "HG=F" },
                    { 17, "Etf", "USD", "SPDR S&P 500", 16, "SPY" },
                    { 18, "Etf", "USD", "Invesco QQQ", 17, "QQQ" },
                    { 19, "Etf", "USD", "Vanguard S&P 500", 18, "VOO" },
                    { 20, "Etf", "USD", "iShares Russell 2000", 19, "IWM" },
                    { 21, "Etf", "USD", "SPDR Gold Shares", 20, "GLD" },
                    { 22, "Etf", "USD", "iShares 20+ Year Treasury", 21, "TLT" },
                    { 23, "Etf", "USD", "Technology Select Sector", 22, "XLK" },
                    { 24, "Etf", "USD", "ARK Innovation", 23, "ARKK" },
                    { 25, "Forex", "USD", "EUR/USD", 24, "EURUSD=X" },
                    { 26, "Forex", "USD", "GBP/USD", 25, "GBPUSD=X" },
                    { 27, "Forex", "JPY", "USD/JPY", 26, "USDJPY=X" },
                    { 28, "Forex", "CHF", "USD/CHF", 27, "USDCHF=X" },
                    { 29, "Forex", "USD", "AUD/USD", 28, "AUDUSD=X" },
                    { 30, "Forex", "CAD", "USD/CAD", 29, "USDCAD=X" },
                    { 31, "Forex", "GBP", "EUR/GBP", 30, "EURGBP=X" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_Symbol",
                table: "Instruments",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Instruments");
        }
    }
}
