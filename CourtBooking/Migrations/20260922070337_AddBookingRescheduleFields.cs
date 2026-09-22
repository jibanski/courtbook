using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingRescheduleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — date/time/string columns hand-fixed to match this
            // repo's Postgres conventions, same as AddBookingRefundFields before it.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "TEXT";
            var dateType     = isPostgres ? "date"                     : "TEXT";
            var timeType     = isPostgres ? "time without time zone"   : "TEXT";
            var nameType     = isPostgres ? "character varying(200)"   : "TEXT";
            var courtType    = isPostgres ? "character varying(100)"   : "TEXT";

            migrationBuilder.AddColumn<DateTime>(
                name: "RescheduledAt",
                table: "Bookings",
                type: dateTimeType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RescheduledByName",
                table: "Bookings",
                type: nameType,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RescheduledFromCourtName",
                table: "Bookings",
                type: courtType,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RescheduledFromDate",
                table: "Bookings",
                type: dateType,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RescheduledFromEndTime",
                table: "Bookings",
                type: timeType,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RescheduledFromStartTime",
                table: "Bookings",
                type: timeType,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RescheduledAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduledByName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduledFromCourtName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduledFromDate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduledFromEndTime",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduledFromStartTime",
                table: "Bookings");
        }
    }
}
