using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transfers_DestinationAccountId_CreatedAt",
                table: "Transfers",
                columns: new[] { "DestinationAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SourceAccountId_CreatedAt",
                table: "Transfers",
                columns: new[] { "SourceAccountId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transfers_DestinationAccountId_CreatedAt",
                table: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Transfers_SourceAccountId_CreatedAt",
                table: "Transfers");
        }
    }
}
