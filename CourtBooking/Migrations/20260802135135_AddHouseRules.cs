using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HouseRules",
                table: "FacilitySettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HouseRules",
                table: "FacilitySettings");
        }
    }
}
