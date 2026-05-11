using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddSportsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sports", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Sports",
                columns: new[] { "Id", "Description", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "Racket sport played on a rectangular court.", 1, true, "Tennis" },
                    { 2, "Fast-paced racket sport using a shuttlecock.", 2, true, "Badminton" },
                    { 3, "Team sport played on a rectangular court.", 3, true, "Basketball" },
                    { 4, "Team sport played over a net.", 4, true, "Volleyball" },
                    { 5, "Team sport played on a grass or turf field.", 5, true, "Football" },
                    { 6, "Indoor variant of football on a smaller court.", 6, true, "Futsal" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sports");
        }
    }
}
