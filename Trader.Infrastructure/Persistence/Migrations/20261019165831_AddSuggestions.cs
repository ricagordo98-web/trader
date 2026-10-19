using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suggestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Entry = table.Column<double>(type: "REAL", nullable: false),
                    StopLoss = table.Column<double>(type: "REAL", nullable: false),
                    TakeProfit = table.Column<double>(type: "REAL", nullable: false),
                    RiskReward = table.Column<double>(type: "REAL", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ClosedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ExitPrice = table.Column<double>(type: "REAL", nullable: true),
                    RMultiple = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_Status",
                table: "Suggestions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_Symbol",
                table: "Suggestions",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Suggestions");
        }
    }
}
