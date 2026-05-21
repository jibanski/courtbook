using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    [Migration("20260521120000_AddQrCodePaths")]
    public partial class AddQrCodePaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GCashQrCodePath",
                table: "FacilitySettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MayaQrCodePath",
                table: "FacilitySettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GCashQrCodePath",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "MayaQrCodePath",
                table: "FacilitySettings");
        }
    }
}
