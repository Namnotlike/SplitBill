using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitBill.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringExpenseTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringExpenseTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TotalAmount = table.Column<long>(type: "bigint", nullable: false),
                    ExtraFeeAmount = table.Column<long>(type: "bigint", nullable: false),
                    SplitMode = table.Column<int>(type: "int", nullable: false),
                    SplitConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenseTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringExpenseTemplates_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenseTemplates_GroupId",
                table: "RecurringExpenseTemplates",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenseTemplates_IsActive_NextRunAt",
                table: "RecurringExpenseTemplates",
                columns: new[] { "IsActive", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringExpenseTemplates");
        }
    }
}
