using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddAddOnPricingType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PricingType",
                table: "BookingAddOns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PricingType",
                table: "AddOnItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricingType",
                table: "BookingAddOns");

            migrationBuilder.DropColumn(
                name: "PricingType",
                table: "AddOnItems");
        }
    }
}
