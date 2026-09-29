using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminFeeLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — string/date columns hand-fixed to match this
            // repo's Postgres conventions, same as AddVouchers/AddBookingRescheduleFields before it.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var textType     = isPostgres ? "text"                     : "TEXT";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "TEXT";
            var refType      = isPostgres ? "character varying(100)"   : "TEXT";
            var proofType    = isPostgres ? "character varying(500)"   : "TEXT";
            var nameType     = isPostgres ? "character varying(200)"   : "TEXT";
            var reasonType   = isPostgres ? "character varying(300)"   : "TEXT";
            var guidType     = isPostgres ? "uuid"                     : "TEXT";

            migrationBuilder.CreateTable(
                name: "AdminFeeSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: textType, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentReference = table.Column<string>(type: refType, maxLength: 100, nullable: true),
                    ProofPath = table.Column<string>(type: proofType, maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    VerifiedByName = table.Column<string>(type: nameType, maxLength: 200, nullable: true),
                    RejectionReason = table.Column<string>(type: reasonType, maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminFeeSettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminFeeCharges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: textType, nullable: false),
                    BookingId = table.Column<int>(type: "INTEGER", nullable: true),
                    BundleGroupId = table.Column<Guid>(type: guidType, nullable: true),
                    OpenPlaySignupId = table.Column<int>(type: "INTEGER", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    FeeTypeSnapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    FeeRateSnapshot = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    FeeFixedAmountSnapshot = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AccruedAt = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    ReversedAt = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    SettlementId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminFeeCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminFeeCharges_AdminFeeSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "AdminFeeSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AdminFeeCharges_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdminFeeCharges_OpenPlaySignups_OpenPlaySignupId",
                        column: x => x.OpenPlaySignupId,
                        principalTable: "OpenPlaySignups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeCharges_BookingId",
                table: "AdminFeeCharges",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeCharges_BundleGroupId",
                table: "AdminFeeCharges",
                column: "BundleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeCharges_OpenPlaySignupId",
                table: "AdminFeeCharges",
                column: "OpenPlaySignupId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeCharges_OwnerId_AccruedAt",
                table: "AdminFeeCharges",
                columns: new[] { "OwnerId", "AccruedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeCharges_OwnerId_SettlementId",
                table: "AdminFeeCharges",
                columns: new[] { "OwnerId", "SettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeCharges_SettlementId",
                table: "AdminFeeCharges",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeeSettlements_OwnerId_Status",
                table: "AdminFeeSettlements",
                columns: new[] { "OwnerId", "Status" });

            // ── Backfill from the old Commission aggregate fields, while they still exist ──────
            // Best-effort, one-time reconciliation: real per-booking amounts are preserved as
            // zero-amount audit rows (so historical monthly "Generated" reporting stays accurate),
            // while the *current* outstanding/paid totals are carried forward as two synthetic
            // ledger rows (not tied to a specific booking — the old schema never tracked which
            // individual bookings were paid off, only a running facility-wide balance).
            migrationBuilder.Sql($@"
                INSERT INTO ""AdminFeeCharges""
                    (""OwnerId"",""BookingId"",""BundleGroupId"",""OpenPlaySignupId"",""Amount"",""OriginalAmount"",""FeeTypeSnapshot"",""FeeRateSnapshot"",""FeeFixedAmountSnapshot"",""AccruedAt"",""ReversedAt"",""SettlementId"")
                SELECT co.""OwnerId"", b.""Id"", NULL, NULL, 0, b.""CommissionAmount"", 1, fs.""CommissionRate"", NULL, b.""CreatedAt"", NULL, NULL
                FROM ""Bookings"" b
                JOIN ""Courts"" co ON co.""Id"" = b.""CourtId""
                LEFT JOIN ""FacilitySettings"" fs ON fs.""OwnerId"" = co.""OwnerId""
                WHERE b.""CommissionAmount"" IS NOT NULL AND co.""OwnerId"" IS NOT NULL;
            ");

            migrationBuilder.Sql($@"
                INSERT INTO ""AdminFeeCharges""
                    (""OwnerId"",""BookingId"",""BundleGroupId"",""OpenPlaySignupId"",""Amount"",""OriginalAmount"",""FeeTypeSnapshot"",""FeeRateSnapshot"",""FeeFixedAmountSnapshot"",""AccruedAt"",""ReversedAt"",""SettlementId"")
                SELECT co.""OwnerId"", NULL, NULL, s.""Id"", 0, s.""CommissionAmount"", 1, fs.""CommissionRate"", NULL, s.""CreatedAt"", NULL, NULL
                FROM ""OpenPlaySignups"" s
                JOIN ""Courts"" co ON co.""Id"" = s.""CourtId""
                LEFT JOIN ""FacilitySettings"" fs ON fs.""OwnerId"" = co.""OwnerId""
                WHERE s.""CommissionAmount"" IS NOT NULL AND co.""OwnerId"" IS NOT NULL;
            ");

            migrationBuilder.Sql($@"
                INSERT INTO ""AdminFeeCharges""
                    (""OwnerId"",""BookingId"",""BundleGroupId"",""OpenPlaySignupId"",""Amount"",""OriginalAmount"",""FeeTypeSnapshot"",""FeeRateSnapshot"",""FeeFixedAmountSnapshot"",""AccruedAt"",""ReversedAt"",""SettlementId"")
                SELECT fs.""OwnerId"", NULL, NULL, NULL, fs.""CommissionBalanceOwed"", fs.""CommissionBalanceOwed"", 1, fs.""CommissionRate"", NULL, CURRENT_TIMESTAMP, NULL, NULL
                FROM ""FacilitySettings"" fs
                WHERE fs.""CommissionBalanceOwed"" > 0 AND fs.""OwnerId"" IS NOT NULL;
            ");

            migrationBuilder.Sql($@"
                INSERT INTO ""AdminFeeSettlements""
                    (""OwnerId"",""Amount"",""PaymentReference"",""ProofPath"",""SubmittedAt"",""Status"",""VerifiedAt"",""VerifiedByName"",""RejectionReason"")
                SELECT fs.""OwnerId"", fs.""CommissionTotalPaid"", 'Migration backfill (historical aggregate)', NULL, CURRENT_TIMESTAMP, 1, CURRENT_TIMESTAMP, 'System (migration backfill)', NULL
                FROM ""FacilitySettings"" fs
                WHERE fs.""CommissionTotalPaid"" > 0 AND fs.""OwnerId"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                table: "OpenPlaySignups");

            migrationBuilder.DropColumn(
                name: "CommissionBalanceOwed",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "CommissionPaymentProofPath",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "CommissionPaymentRef",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "CommissionPaymentSubmittedAt",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CommissionPaid",
                table: "Bookings");

            migrationBuilder.RenameColumn(
                name: "CommissionTotalPaid",
                table: "FacilitySettings",
                newName: "AdminFeeFixedAmount");

            migrationBuilder.RenameColumn(
                name: "CommissionRate",
                table: "FacilitySettings",
                newName: "AdminFeeRate");

            migrationBuilder.AddColumn<int>(
                name: "AdminFeeType",
                table: "FacilitySettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Every facility that ever had a fee was percentage-based (Commission never supported a
            // fixed amount) — default new rows still use 0/Fixed, but that would silently zero out
            // the fee for existing Commission facilities (AdminFeeFixedAmount is reset to 0 right
            // below), so every existing row must be pinned to 1/Percentage explicitly.
            migrationBuilder.Sql(@"UPDATE ""FacilitySettings"" SET ""AdminFeeType"" = 1;");

            // AdminFeeFixedAmount inherits CommissionTotalPaid's historical values via the rename
            // above — that column is repurposed, so it must be zeroed for a fresh start.
            migrationBuilder.Sql(@"UPDATE ""FacilitySettings"" SET ""AdminFeeFixedAmount"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var textType     = isPostgres ? "text"                     : "TEXT";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "TEXT";
            var refType      = isPostgres ? "character varying(100)"   : "TEXT";
            var proofType    = isPostgres ? "character varying(500)"   : "TEXT";
            var boolType     = isPostgres ? "boolean"                  : "INTEGER";

            migrationBuilder.DropTable(
                name: "AdminFeeCharges");

            migrationBuilder.DropTable(
                name: "AdminFeeSettlements");

            migrationBuilder.DropColumn(
                name: "AdminFeeType",
                table: "FacilitySettings");

            migrationBuilder.RenameColumn(
                name: "AdminFeeRate",
                table: "FacilitySettings",
                newName: "CommissionRate");

            migrationBuilder.RenameColumn(
                name: "AdminFeeFixedAmount",
                table: "FacilitySettings",
                newName: "CommissionTotalPaid");

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                table: "OpenPlaySignups",
                type: textType,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionBalanceOwed",
                table: "FacilitySettings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CommissionPaymentProofPath",
                table: "FacilitySettings",
                type: proofType,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommissionPaymentRef",
                table: "FacilitySettings",
                type: refType,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommissionPaymentSubmittedAt",
                table: "FacilitySettings",
                type: dateTimeType,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommissionPaid",
                table: "Bookings",
                type: boolType,
                nullable: false,
                defaultValue: false);
        }
    }
}
