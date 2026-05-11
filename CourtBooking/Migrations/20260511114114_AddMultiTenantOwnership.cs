using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenantOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "FacilitySettings",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "FacilitySettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Courts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySettings_OwnerId",
                table: "FacilitySettings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Courts_OwnerId",
                table: "Courts",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Courts_AspNetUsers_OwnerId",
                table: "Courts",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FacilitySettings_AspNetUsers_OwnerId",
                table: "FacilitySettings",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courts_AspNetUsers_OwnerId",
                table: "Courts");

            migrationBuilder.DropForeignKey(
                name: "FK_FacilitySettings_AspNetUsers_OwnerId",
                table: "FacilitySettings");

            migrationBuilder.DropIndex(
                name: "IX_FacilitySettings_OwnerId",
                table: "FacilitySettings");

            migrationBuilder.DropIndex(
                name: "IX_Courts_OwnerId",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "FacilitySettings");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Courts");

            migrationBuilder.InsertData(
                table: "FacilitySettings",
                columns: new[] { "Id", "BrandLogoUrl", "BrandName", "BrandTagline", "FacilityName", "GCashName", "GCashNumber", "IsSubscribed", "MayaName", "MayaNumber", "PaymentInstructions", "SubscriptionActivatedAt", "SubscriptionPaymentRef", "SubscriptionPlan", "SubscriptionProofPath", "SubscriptionSubmittedAt", "TrialStartedAt" },
                values: new object[] { 1, null, null, null, "CourtBook", null, null, false, null, null, "Please send the exact amount and include your booking reference in the notes.", null, null, null, null, null, null });
        }
    }
}
