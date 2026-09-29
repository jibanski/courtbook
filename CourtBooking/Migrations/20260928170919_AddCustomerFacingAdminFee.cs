using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerFacingAdminFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — money columns hand-fixed to match this repo's
            // Postgres conventions, same as AddVouchers before it.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var moneyType = isPostgres ? "numeric(10,2)" : "TEXT";

            migrationBuilder.AddColumn<decimal>(
                name: "AdminFeeAmount",
                table: "OpenPlaySignups",
                type: moneyType,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AdminFeeAmount",
                table: "Bookings",
                type: moneyType,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminFeeAmount",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "AdminFeeAmount",
                table: "Bookings");
        }
    }
}
