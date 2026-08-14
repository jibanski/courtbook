using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCourtNameAndCustomerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CourtName",
                table: "OpenPlaySignups",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "OpenPlaySignups",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourtName",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "Bookings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CourtName",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "CourtName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "Bookings");
        }
    }
}
