using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingRefundFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — string/decimal/date columns come out typed for
            // SQLite by default, hand-fixed to match this repo's Postgres conventions, same as
            // AddVouchers before it.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var reasonType  = isPostgres ? "character varying(300)"   : "TEXT";
            var moneyType   = isPostgres ? "numeric(10,2)"            : "TEXT";
            var dateType    = isPostgres ? "timestamp with time zone" : "TEXT";

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "OpenPlaySignups",
                type: moneyType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReason",
                table: "OpenPlaySignups",
                type: reasonType,
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "OpenPlaySignups",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "Bookings",
                type: moneyType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReason",
                table: "Bookings",
                type: reasonType,
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "Bookings",
                type: dateType,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "Bookings");
        }
    }
}
