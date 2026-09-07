using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitBill.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseComments_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseComments_GroupMembers_AuthorMemberId",
                        column: x => x.AuthorMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseComments_AuthorMemberId",
                table: "ExpenseComments",
                column: "AuthorMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseComments_ExpenseId_IsDeleted_CreatedAt",
                table: "ExpenseComments",
                columns: new[] { "ExpenseId", "IsDeleted", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseComments");
        }
    }
}
