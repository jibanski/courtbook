using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffRescheduleSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — hand-fixed to Postgres boolean per repo convention.
            // Default TRUE so existing facilities keep letting staff reschedule.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

            migrationBuilder.AddColumn<bool>(
                name: "AllowStaffReschedule",
                table: "FacilitySettings",
                type: isPostgres ? "boolean" : "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowStaffReschedule",
                table: "FacilitySettings");
        }
    }
}
