using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddAddOnRentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: GoTyme*/CourtScheduleBlocks.Description column adds were stripped from this
            // generated migration — those already exist in real databases via the raw-SQL fallback
            // in Program.cs (added before a formal migration ever covered them), so re-running them
            // here would throw "column already exists" and crash Migrate() on deploy.
            migrationBuilder.CreateTable(
                name: "AddOnRentals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CustomerNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TotalPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentReference = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentProofPath = table.Column<string>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LoggedByStaffId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnRentals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddOnRentals_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddOnRentalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AddOnRentalId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddOnItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PricingType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnRentalItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddOnRentalItems_AddOnItems_AddOnItemId",
                        column: x => x.AddOnItemId,
                        principalTable: "AddOnItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AddOnRentalItems_AddOnRentals_AddOnRentalId",
                        column: x => x.AddOnRentalId,
                        principalTable: "AddOnRentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddOnRentalItems_AddOnItemId",
                table: "AddOnRentalItems",
                column: "AddOnItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AddOnRentalItems_AddOnRentalId",
                table: "AddOnRentalItems",
                column: "AddOnRentalId");

            migrationBuilder.CreateIndex(
                name: "IX_AddOnRentals_UserId",
                table: "AddOnRentals",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddOnRentalItems");

            migrationBuilder.DropTable(
                name: "AddOnRentals");
        }
    }
}
