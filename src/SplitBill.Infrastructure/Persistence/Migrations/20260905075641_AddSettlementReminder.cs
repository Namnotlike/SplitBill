using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitBill.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReminderSentAt",
                table: "Settlements",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReminderSentAt",
                table: "Settlements");
        }
    }
}
