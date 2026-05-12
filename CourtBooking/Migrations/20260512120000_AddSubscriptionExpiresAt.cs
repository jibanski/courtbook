using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionExpiresAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionExpiresAt",
                table: "FacilitySettings",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill expiry for already-active subscribers:
            //   annual → activated + 365 days, anything else → activated + 30 days.
            migrationBuilder.Sql(@"
                UPDATE ""FacilitySettings""
                SET ""SubscriptionExpiresAt"" = ""SubscriptionActivatedAt"" + INTERVAL '365 days'
                WHERE ""IsSubscribed"" = TRUE
                  AND ""SubscriptionActivatedAt"" IS NOT NULL
                  AND LOWER(COALESCE(""SubscriptionPlan"", '')) = 'annual';

                UPDATE ""FacilitySettings""
                SET ""SubscriptionExpiresAt"" = ""SubscriptionActivatedAt"" + INTERVAL '30 days'
                WHERE ""IsSubscribed"" = TRUE
                  AND ""SubscriptionActivatedAt"" IS NOT NULL
                  AND LOWER(COALESCE(""SubscriptionPlan"", '')) <> 'annual';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionExpiresAt",
                table: "FacilitySettings");
        }
    }
}
