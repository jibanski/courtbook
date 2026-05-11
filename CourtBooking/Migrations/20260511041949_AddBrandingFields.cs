using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandLogoUrl",
                table: "FacilitySettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandName",
                table: "FacilitySettings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandTagline",
                table: "FacilitySettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "FacilitySettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BrandLogoUrl", "BrandName", "BrandTagline" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandLogoUrl",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "BrandName",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "BrandTagline",
                table: "FacilitySettings");
        }
    }
}
