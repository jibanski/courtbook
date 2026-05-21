using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    [Migration("20260521110000_AddPayMongoSecretKey")]
    public partial class AddPayMongoSecretKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayMongoSecretKey",
                table: "FacilitySettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayMongoSecretKey",
                table: "FacilitySettings");
        }
    }
}
