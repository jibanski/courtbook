using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTestCourtSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Courts",
                keyColumn: "Id",
                keyValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Courts",
                columns: new[] { "Id", "ClosingHour", "Description", "ImageUrl", "IsActive", "IsIndoor", "Name", "OpeningHour", "PricePerHour", "SportType" },
                values: new object[,]
                {
                    { 1, 22, "Professional tennis court with hard surface.", null, true, false, "Court A", 6, 300m, "Tennis" },
                    { 2, 22, "Indoor badminton court with proper lighting.", null, true, true, "Court B", 6, 200m, "Badminton" },
                    { 3, 22, "Full-size basketball court.", null, true, true, "Court C", 8, 400m, "Basketball" },
                    { 4, 21, "Sand volleyball court.", null, true, false, "Court D", 6, 250m, "Volleyball" }
                });
        }
    }
}
