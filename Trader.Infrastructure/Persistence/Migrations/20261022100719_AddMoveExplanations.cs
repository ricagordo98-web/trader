using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoveExplanations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoveExplanations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    From = table.Column<long>(type: "INTEGER", nullable: false),
                    To = table.Column<long>(type: "INTEGER", nullable: false),
                    NarrativeStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoveExplanations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoveExplanations_Symbol_From_To",
                table: "MoveExplanations",
                columns: new[] { "Symbol", "From", "To" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoveExplanations");
        }
    }
}
