using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class FixAddOnRentalDateTimeColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same class of bug as FixAddOnRentalTotalPriceColumnType: AddAddOnRentals was
            // scaffolded under SQLite, so these DateTime columns got baked in as raw "TEXT"
            // instead of "timestamp with time zone" (compare InitialCreate, scaffolded under
            // Postgres, which used the correct type for every other DateTime column). Postgres
            // has no implicit cast from text to timestamptz either, so this needs an explicit
            // USING cast.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    "ALTER TABLE \"AddOnRentals\" ALTER COLUMN \"PaidAt\" TYPE timestamp with time zone USING \"PaidAt\"::timestamp with time zone;");
                migrationBuilder.Sql(
                    "ALTER TABLE \"AddOnRentals\" ALTER COLUMN \"CreatedAt\" TYPE timestamp with time zone USING \"CreatedAt\"::timestamp with time zone;");
            }
            else
            {
                migrationBuilder.AlterColumn<DateTime>(
                    name: "PaidAt",
                    table: "AddOnRentals",
                    type: "timestamp with time zone",
                    nullable: true,
                    oldClrType: typeof(DateTime),
                    oldType: "TEXT",
                    oldNullable: true);

                migrationBuilder.AlterColumn<DateTime>(
                    name: "CreatedAt",
                    table: "AddOnRentals",
                    type: "timestamp with time zone",
                    nullable: false,
                    oldClrType: typeof(DateTime),
                    oldType: "TEXT");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    "ALTER TABLE \"AddOnRentals\" ALTER COLUMN \"PaidAt\" TYPE text USING \"PaidAt\"::text;");
                migrationBuilder.Sql(
                    "ALTER TABLE \"AddOnRentals\" ALTER COLUMN \"CreatedAt\" TYPE text USING \"CreatedAt\"::text;");
            }
            else
            {
                migrationBuilder.AlterColumn<DateTime>(
                    name: "PaidAt",
                    table: "AddOnRentals",
                    type: "TEXT",
                    nullable: true,
                    oldClrType: typeof(DateTime),
                    oldType: "timestamp with time zone",
                    oldNullable: true);

                migrationBuilder.AlterColumn<DateTime>(
                    name: "CreatedAt",
                    table: "AddOnRentals",
                    type: "TEXT",
                    nullable: false,
                    oldClrType: typeof(DateTime),
                    oldType: "timestamp with time zone");
            }
        }
    }
}
