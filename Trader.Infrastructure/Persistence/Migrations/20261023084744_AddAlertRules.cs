using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssetClass = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MinRelativeVolume = table.Column<double>(type: "REAL", nullable: true),
                    MinChangePercent = table.Column<double>(type: "REAL", nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    EventKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Sentiment = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CooldownMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_UserId",
                table: "AlertRules",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRules");
        }
    }
}
