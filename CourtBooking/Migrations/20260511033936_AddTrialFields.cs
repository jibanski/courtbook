using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSubscribed",
                table: "FacilitySettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialStartedAt",
                table: "FacilitySettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "FacilitySettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "IsSubscribed", "TrialStartedAt" },
                values: new object[] { false, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSubscribed",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "TrialStartedAt",
                table: "FacilitySettings");
        }
    }
}
