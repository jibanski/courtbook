using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingModelAndCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── FacilitySettings ──────────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "BillingModel",
                table: "FacilitySettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Subscription");

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "FacilitySettings",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 2.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionBalanceOwed",
                table: "FacilitySettings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionTotalPaid",
                table: "FacilitySettings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CommissionPaymentRef",
                table: "FacilitySettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommissionPaymentProofPath",
                table: "FacilitySettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommissionPaymentSubmittedAt",
                table: "FacilitySettings",
                type: "timestamp with time zone",
                nullable: true);

            // ── Bookings ──────────────────────────────────────────────────────
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommissionPaid",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BillingModel",                   table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionRate",                  table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionBalanceOwed",           table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionTotalPaid",             table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionPaymentRef",            table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionPaymentProofPath",      table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionPaymentSubmittedAt",    table: "FacilitySettings");
            migrationBuilder.DropColumn(name: "CommissionAmount",                table: "Bookings");
            migrationBuilder.DropColumn(name: "CommissionPaid",                  table: "Bookings");
        }
    }
}
