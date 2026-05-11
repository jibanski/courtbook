using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodAndRenameSourceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PayMongoLinkId",
                table: "Bookings",
                newName: "PaymentMethod");

            migrationBuilder.AddColumn<string>(
                name: "PayMongoSourceId",
                table: "Bookings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayMongoSourceId",
                table: "Bookings");

            migrationBuilder.RenameColumn(
                name: "PaymentMethod",
                table: "Bookings",
                newName: "PayMongoLinkId");
        }
    }
}
