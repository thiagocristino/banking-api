using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAt",
                table: "LedgerEntries");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "LedgerEntries",
                newName: "CreatedAtUtc");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "LedgerEntries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAfter",
                table: "LedgerEntries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUtc_Id",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUtc_Id",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "BalanceAfter",
                table: "LedgerEntries");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "LedgerEntries",
                newName: "CreatedAt");

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "LedgerEntries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAt",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "CreatedAt" });
        }
    }
}
