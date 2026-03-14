using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Wallet.Core.Migrations
{
    /// <inheritdoc />
    public partial class IncreasePqcColumnSizes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PublicKey",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedPrivateKey",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);

            migrationBuilder.AlterColumn<string>(
                name: "PublicKey",
                schema: "wallet",
                table: "WalletAddresses",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PublicKey",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedPrivateKey",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16384)",
                oldMaxLength: 16384);

            migrationBuilder.AlterColumn<string>(
                name: "PublicKey",
                schema: "wallet",
                table: "WalletAddresses",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192,
                oldNullable: true);
        }
    }
}
