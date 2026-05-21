using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    [Migration("20260521100000_AddCheckoutSessionId")]
    public partial class AddCheckoutSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutSessionId",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckoutSessionId",
                table: "Bookings");
        }
    }
}
