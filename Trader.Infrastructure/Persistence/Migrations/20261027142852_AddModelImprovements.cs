using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelImprovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MaeR",
                table: "Suggestions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MfeR",
                table: "Suggestions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SmartR",
                table: "Suggestions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Variant",
                table: "Suggestions",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "A");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaeR",
                table: "Suggestions");

            migrationBuilder.DropColumn(
                name: "MfeR",
                table: "Suggestions");

            migrationBuilder.DropColumn(
                name: "SmartR",
                table: "Suggestions");

            migrationBuilder.DropColumn(
                name: "Variant",
                table: "Suggestions");
        }
    }
}
