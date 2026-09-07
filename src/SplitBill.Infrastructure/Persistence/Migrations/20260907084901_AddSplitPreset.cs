using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitBill.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSplitPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SplitPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SplitMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SplitConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitPresets_GroupMembers_CreatedByMemberId",
                        column: x => x.CreatedByMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SplitPresets_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SplitPresets_CreatedByMemberId",
                table: "SplitPresets",
                column: "CreatedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitPresets_GroupId_IsDeleted_CreatedAt",
                table: "SplitPresets",
                columns: new[] { "GroupId", "IsDeleted", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SplitPresets");
        }
    }
}
