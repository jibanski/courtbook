using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class ManualPaymentFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PayMongoSourceId",
                table: "Bookings",
                newName: "PaymentReference");

            // PayMongoCheckoutUrl was string; drop it and add DateTime? column
            migrationBuilder.DropColumn(
                name: "PayMongoCheckoutUrl",
                table: "Bookings");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentProofSubmittedAt",
                table: "Bookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentProofPath",
                table: "Bookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FacilitySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FacilityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GCashNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    GCashName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MayaNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    MayaName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PaymentInstructions = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilitySettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "FacilitySettings",
                columns: new[] { "Id", "FacilityName", "GCashName", "GCashNumber", "MayaName", "MayaNumber", "PaymentInstructions" },
                values: new object[] { 1, "CourtBook", null, null, null, null, "Please send the exact amount and include your booking reference in the notes." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "PaymentProofPath",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentProofSubmittedAt",
                table: "Bookings");

            migrationBuilder.RenameColumn(
                name: "PaymentReference",
                table: "Bookings",
                newName: "PayMongoSourceId");

            migrationBuilder.AddColumn<string>(
                name: "PayMongoCheckoutUrl",
                table: "Bookings",
                type: "TEXT",
                nullable: true);
        }
    }
}
