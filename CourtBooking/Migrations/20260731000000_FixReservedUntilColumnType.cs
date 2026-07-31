using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class FixReservedUntilColumnType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // On PostgreSQL, ReservedUntil was created as TEXT due to hardcoded type in the
            // previous migration. Cast it to timestamptz so EF Core can deserialize it.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
                    ALTER TABLE ""Bookings""
                        ALTER COLUMN ""ReservedUntil"" TYPE timestamp with time zone
                        USING CASE
                            WHEN ""ReservedUntil"" IS NULL THEN NULL
                            ELSE ""ReservedUntil""::timestamp with time zone
                        END;

                    ALTER TABLE ""OpenPlaySignups""
                        ALTER COLUMN ""ReservedUntil"" TYPE timestamp with time zone
                        USING CASE
                            WHEN ""ReservedUntil"" IS NULL THEN NULL
                            ELSE ""ReservedUntil""::timestamp with time zone
                        END;
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
                    ALTER TABLE ""Bookings""
                        ALTER COLUMN ""ReservedUntil"" TYPE text
                        USING ""ReservedUntil""::text;

                    ALTER TABLE ""OpenPlaySignups""
                        ALTER COLUMN ""ReservedUntil"" TYPE text
                        USING ""ReservedUntil""::text;
                ");
            }
        }
    }
}
