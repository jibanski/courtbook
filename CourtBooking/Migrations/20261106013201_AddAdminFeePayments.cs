using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminFeePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded against local SQLite — string/date/bool columns hand-fixed to match this
            // repo's Postgres conventions, same as AddAdminFeeLedger before it.
            bool isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var textType     = isPostgres ? "text"                     : "TEXT";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "TEXT";
            var noteType     = isPostgres ? "character varying(300)"   : "TEXT";
            var nameType     = isPostgres ? "character varying(200)"   : "TEXT";
            var boolType     = isPostgres ? "boolean"                  : "INTEGER";

            migrationBuilder.AddColumn<decimal>(
                name: "CreditApplied",
                table: "AdminFeeSettlements",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AdminFeePayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<string>(type: textType, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaidOn = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    Note = table.Column<string>(type: noteType, maxLength: 300, nullable: true),
                    RecordedByName = table.Column<string>(type: nameType, maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    IsVoided = table.Column<bool>(type: boolType, nullable: false),
                    VoidedAt = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    VoidedByName = table.Column<string>(type: nameType, maxLength: 200, nullable: true),
                    VoidReason = table.Column<string>(type: noteType, maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminFeePayments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminFeePayments_OwnerId_IsVoided",
                table: "AdminFeePayments",
                columns: new[] { "OwnerId", "IsVoided" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminFeePayments");

            migrationBuilder.DropColumn(
                name: "CreditApplied",
                table: "AdminFeeSettlements");
        }
    }
}
