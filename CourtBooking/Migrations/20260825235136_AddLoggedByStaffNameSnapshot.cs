using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddLoggedByStaffNameSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var stringType = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"
                ? "character varying(200)"
                : "TEXT";

            migrationBuilder.AddColumn<string>(
                name: "LoggedByStaffName",
                table: "OpenPlaySignups",
                type: stringType,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoggedByStaffName",
                table: "Bookings",
                type: stringType,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoggedByStaffName",
                table: "AddOnRentals",
                type: stringType,
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoggedByStaffName",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "LoggedByStaffName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "LoggedByStaffName",
                table: "AddOnRentals");
        }
    }
}
