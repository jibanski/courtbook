using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class FixAddOnRentalTotalPriceColumnType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddAddOnRentals was scaffolded against the local SQLite provider, so TotalPrice
            // was baked in as "TEXT" — a valid raw Postgres type name too, so Migrate() silently
            // created it as a Postgres `text` column instead of `numeric` in production. A plain
            // ALTER COLUMN TYPE fails there ("cannot be cast automatically"), so it needs an
            // explicit USING cast; SQLite has no equivalent restriction.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    "ALTER TABLE \"AddOnRentals\" ALTER COLUMN \"TotalPrice\" TYPE numeric(10,2) USING \"TotalPrice\"::numeric;");
            }
            else
            {
                migrationBuilder.AlterColumn<decimal>(
                    name: "TotalPrice",
                    table: "AddOnRentals",
                    type: "numeric(10,2)",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    "ALTER TABLE \"AddOnRentals\" ALTER COLUMN \"TotalPrice\" TYPE text USING \"TotalPrice\"::text;");
            }
            else
            {
                migrationBuilder.AlterColumn<decimal>(
                    name: "TotalPrice",
                    table: "AddOnRentals",
                    type: "TEXT",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "numeric(10,2)");
            }
        }
    }
}
