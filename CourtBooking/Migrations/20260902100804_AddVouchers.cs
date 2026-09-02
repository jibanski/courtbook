using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — string/decimal/date/bool columns come out typed
            // for SQLite by default, which must be hand-fixed to match this repo's Postgres
            // conventions (character varying(N) for strings, numeric(10,2) for money, timestamp
            // with time zone for dates, boolean for bools) per the EF Migrations gotcha
            // documented elsewhere in this repo.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var codeType    = isPostgres ? "character varying(30)"    : "TEXT";
            var moneyType   = isPostgres ? "numeric(10,2)"            : "TEXT";
            var dateType    = isPostgres ? "timestamp with time zone" : "TEXT";
            var boolType    = isPostgres ? "boolean"                  : "INTEGER";

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "OpenPlaySignups",
                type: moneyType,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VoucherCode",
                table: "OpenPlaySignups",
                type: codeType,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoucherId",
                table: "OpenPlaySignups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Bookings",
                type: moneyType,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VoucherCode",
                table: "Bookings",
                type: codeType,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoucherId",
                table: "Bookings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: isPostgres ? "character varying(450)" : "TEXT", maxLength: 450, nullable: false),
                    Code = table.Column<string>(type: codeType, maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: isPostgres ? "character varying(200)" : "TEXT", maxLength: 200, nullable: true),
                    DiscountType = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    MaxDiscountAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    MinSpend = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "INTEGER", nullable: true),
                    TimesRedeemed = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: dateType, nullable: false),
                    IsActive = table.Column<bool>(type: boolType, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: dateType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vouchers_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_OwnerId_Code",
                table: "Vouchers",
                columns: new[] { "OwnerId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Vouchers");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "VoucherCode",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "VoucherId",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VoucherCode",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VoucherId",
                table: "Bookings");
        }
    }
}
