using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    [Migration("20260721000000_AddFacilityNameToBooking")]
    public partial class AddFacilityNameToBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacilityName",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Backfill existing bookings with the facility name of the court's
            // owner. Double-quoted identifiers work on both PostgreSQL (prod)
            // and SQLite (local dev).
            migrationBuilder.Sql(@"
                UPDATE ""Bookings""
                SET ""FacilityName"" = (
                    SELECT fs.""FacilityName""
                    FROM ""Courts"" AS c
                    INNER JOIN ""FacilitySettings"" AS fs ON fs.""OwnerId"" = c.""OwnerId""
                    WHERE c.""Id"" = ""Bookings"".""CourtId""
                )
                WHERE ""FacilityName"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacilityName",
                table: "Bookings");
        }
    }
}
