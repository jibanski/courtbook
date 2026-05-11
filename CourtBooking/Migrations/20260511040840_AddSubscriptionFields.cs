using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionActivatedAt",
                table: "FacilitySettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionPaymentRef",
                table: "FacilitySettings",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionPlan",
                table: "FacilitySettings",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionProofPath",
                table: "FacilitySettings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionSubmittedAt",
                table: "FacilitySettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "FacilitySettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "SubscriptionActivatedAt", "SubscriptionPaymentRef", "SubscriptionPlan", "SubscriptionProofPath", "SubscriptionSubmittedAt" },
                values: new object[] { null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionActivatedAt",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "SubscriptionPaymentRef",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "SubscriptionProofPath",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "SubscriptionSubmittedAt",
                table: "FacilitySettings");
        }
    }
}
