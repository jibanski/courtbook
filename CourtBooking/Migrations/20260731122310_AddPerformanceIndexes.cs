using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    public partial class AddPerformanceIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS makes this safe to run on databases that already have some of these indexes
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Bookings_CourtId_BookingDate_Status"" ON ""Bookings"" (""CourtId"", ""BookingDate"", ""Status"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Bookings_UserId"" ON ""Bookings"" (""UserId"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Bookings_Status_PaymentProofSubmittedAt"" ON ""Bookings"" (""Status"", ""PaymentProofSubmittedAt"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_OpenPlaySignups_CourtId_BookingDate_Status"" ON ""OpenPlaySignups"" (""CourtId"", ""BookingDate"", ""Status"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_OpenPlaySignups_UserId"" ON ""OpenPlaySignups"" (""UserId"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_CourtTimeSlots_CourtId_SlotDate"" ON ""CourtTimeSlots"" (""CourtId"", ""SlotDate"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_CourtBlocks_CourtId_StartDate_EndDate"" ON ""CourtBlocks"" (""CourtId"", ""StartDate"", ""EndDate"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_CourtRateTiers_CourtId"" ON ""CourtRateTiers"" (""CourtId"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_CourtScheduleBlocks_CourtId"" ON ""CourtScheduleBlocks"" (""CourtId"")");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_FacilityHolidays_OwnerId_Date"" ON ""FacilityHolidays"" (""OwnerId"", ""Date"")");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FacilitySettings_OwnerId"" ON ""FacilitySettings"" (""OwnerId"")");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FacilitySettings_Slug"" ON ""FacilitySettings"" (""Slug"")");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Bookings_CourtId_BookingDate_Status""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Bookings_UserId""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Bookings_Status_PaymentProofSubmittedAt""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_OpenPlaySignups_CourtId_BookingDate_Status""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_OpenPlaySignups_UserId""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CourtTimeSlots_CourtId_SlotDate""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CourtBlocks_CourtId_StartDate_EndDate""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CourtRateTiers_CourtId""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CourtScheduleBlocks_CourtId""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_FacilityHolidays_OwnerId_Date""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_FacilitySettings_OwnerId""");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_FacilitySettings_Slug""");
        }
    }
}
