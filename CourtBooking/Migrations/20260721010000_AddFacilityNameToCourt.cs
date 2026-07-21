using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    [Migration("20260721010000_AddFacilityNameToCourt")]
    public partial class AddFacilityNameToCourt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacilityName",
                table: "Courts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Backfill existing courts with their owner's facility name. Double-quoted
            // identifiers work on both PostgreSQL (prod) and SQLite (local dev).
            migrationBuilder.Sql(@"
                UPDATE ""Courts""
                SET ""FacilityName"" = (
                    SELECT fs.""FacilityName""
                    FROM ""FacilitySettings"" AS fs
                    WHERE fs.""OwnerId"" = ""Courts"".""OwnerId""
                )
                WHERE ""FacilityName"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacilityName",
                table: "Courts");
        }
    }
}
