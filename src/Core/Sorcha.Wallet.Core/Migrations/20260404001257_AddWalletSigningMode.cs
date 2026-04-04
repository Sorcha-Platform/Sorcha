using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Wallet.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletSigningMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KmsKeyId",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SigningMode",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Local");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KmsKeyId",
                schema: "wallet",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "SigningMode",
                schema: "wallet",
                table: "Wallets");
        }
    }
}
