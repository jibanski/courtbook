using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddBankQrToPlatformConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "MetrobankQrData",
                table: "PlatformConfig",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetrobankQrContentType",
                table: "PlatformConfig",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "BpiQrData",
                table: "PlatformConfig",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BpiQrContentType",
                table: "PlatformConfig",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MetrobankQrData",        table: "PlatformConfig");
            migrationBuilder.DropColumn(name: "MetrobankQrContentType", table: "PlatformConfig");
            migrationBuilder.DropColumn(name: "BpiQrData",              table: "PlatformConfig");
            migrationBuilder.DropColumn(name: "BpiQrContentType",       table: "PlatformConfig");
        }
    }
}
