using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitBill.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementIsWaived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWaived",
                table: "Settlements",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsWaived",
                table: "Settlements");
        }
    }
}
