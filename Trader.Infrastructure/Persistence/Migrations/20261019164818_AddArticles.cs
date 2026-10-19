using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    UrlHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TitleHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IngestedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Sentiment = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArticleSymbol",
                columns: table => new
                {
                    ArticleId = table.Column<long>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleSymbol", x => new { x.ArticleId, x.Symbol });
                    table.ForeignKey(
                        name: "FK_ArticleSymbol_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_PublishedAt",
                table: "Articles",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_TitleHash",
                table: "Articles",
                column: "TitleHash");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_UrlHash",
                table: "Articles",
                column: "UrlHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleSymbol_Symbol",
                table: "ArticleSymbol",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleSymbol");

            migrationBuilder.DropTable(
                name: "Articles");
        }
    }
}
