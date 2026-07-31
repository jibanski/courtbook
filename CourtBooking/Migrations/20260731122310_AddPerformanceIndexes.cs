using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CourtBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AddOnItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    PhoneNumberAlt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreferredFacilitySlug = table.Column<string>(type: "TEXT", nullable: true),
                    PrivacyPolicyAcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsGuest = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmployerOwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourtBundles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacilityHolidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityHolidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GCashQrData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    GCashQrContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    MayaQrData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    MayaQrContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    MetrobankQrData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    MetrobankQrContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BpiQrData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    BpiQrContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LogoData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    LogoContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Courts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SportType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PricePerHour = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsIndoor = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    OpeningHour = table.Column<int>(type: "INTEGER", nullable: false),
                    ClosingHour = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FacilitySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FacilityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    GCashNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    GCashName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    GCashQrCodePath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MayaNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    MayaName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MayaQrCodePath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PaymentInstructions = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PayMongoSecretKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PayMongoMethods = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    FacebookUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    InstagramUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsSuspended = table.Column<bool>(type: "INTEGER", nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SuspendedReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsDeactivated = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BillingModel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    CommissionBalanceOwed = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CommissionTotalPaid = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CommissionPaymentRef = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CommissionPaymentProofPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CommissionPaymentSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TrialStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsSubscribed = table.Column<bool>(type: "INTEGER", nullable: false),
                    BrandName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BrandTagline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BrandLogoUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SubscriptionPlan = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SubscriptionPaymentRef = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SubscriptionProofPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SubscriptionSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubscriptionActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubscriptionExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastExpiryReminderThreshold = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilitySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilitySettings_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    OwnerName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    FacilityName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsFeatured = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtBundleRateBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtBundleId = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysOfWeek = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IncludeHolidays = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    EndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    FlatPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtBundleRateBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtBundleRateBlocks_CourtBundles_CourtBundleId",
                        column: x => x.CourtBundleId,
                        principalTable: "CourtBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    FacilityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    BookingDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentReference = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentProofPath = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentProofSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReservedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LoggedByStaffId = table.Column<string>(type: "TEXT", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    CommissionPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    CourtBundleId = table.Column<int>(type: "INTEGER", nullable: true),
                    BundleGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GuestAccessToken = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookings_CourtBundles_CourtBundleId",
                        column: x => x.CourtBundleId,
                        principalTable: "CourtBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Bookings_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtBlocks_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtBundleCourts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtBundleId = table.Column<int>(type: "INTEGER", nullable: false),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtBundleCourts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtBundleCourts_CourtBundles_CourtBundleId",
                        column: x => x.CourtBundleId,
                        principalTable: "CourtBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourtBundleCourts_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtRateTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysOfWeek = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IncludeHolidays = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    EndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    PricePerHour = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtRateTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtRateTiers_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtScheduleBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysOfWeek = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IncludeHolidays = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    EndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowPublicSignup = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxPlayers = table.Column<int>(type: "INTEGER", nullable: true),
                    PricePerHead = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtScheduleBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtScheduleBlocks_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourtTimeSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    EndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtTimeSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtTimeSlots_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenPlaySignups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    FacilityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    BookingDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    EndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    SpotCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PricePerHeadSnapshot = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PlayerNames = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentReference = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentProofPath = table.Column<string>(type: "TEXT", nullable: true),
                    PaymentProofSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReservedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    GuestAccessToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    LoggedByStaffId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPlaySignups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPlaySignups_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpenPlaySignups_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingAddOns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookingId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddOnItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingAddOns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingAddOns_AddOnItems_AddOnItemId",
                        column: x => x.AddOnItemId,
                        principalTable: "AddOnItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingAddOns_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Sports",
                columns: new[] { "Id", "Description", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "Racket sport played on a rectangular court.", 1, true, "Tennis" },
                    { 2, "Fast-paced racket sport using a shuttlecock.", 2, true, "Badminton" },
                    { 3, "Team sport played on a rectangular court.", 3, true, "Basketball" },
                    { 4, "Team sport played over a net.", 4, true, "Volleyball" },
                    { 5, "Team sport played on a grass or turf field.", 5, true, "Football" },
                    { 6, "Indoor variant of football on a smaller court.", 6, true, "Futsal" },
                    { 7, "Fast-growing paddle sport combining tennis, badminton, and ping-pong.", 7, true, "Pickleball" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingAddOns_AddOnItemId",
                table: "BookingAddOns",
                column: "AddOnItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingAddOns_BookingId",
                table: "BookingAddOns",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CourtBundleId",
                table: "Bookings",
                column: "CourtBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CourtId_BookingDate_Status",
                table: "Bookings",
                columns: new[] { "CourtId", "BookingDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_PaymentProofSubmittedAt",
                table: "Bookings",
                columns: new[] { "Status", "PaymentProofSubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtBlocks_CourtId_StartDate_EndDate",
                table: "CourtBlocks",
                columns: new[] { "CourtId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtBundleCourts_CourtBundleId",
                table: "CourtBundleCourts",
                column: "CourtBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtBundleCourts_CourtId",
                table: "CourtBundleCourts",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtBundleRateBlocks_CourtBundleId",
                table: "CourtBundleRateBlocks",
                column: "CourtBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtRateTiers_CourtId",
                table: "CourtRateTiers",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_Courts_OwnerId",
                table: "Courts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtScheduleBlocks_CourtId",
                table: "CourtScheduleBlocks",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtTimeSlots_CourtId_SlotDate",
                table: "CourtTimeSlots",
                columns: new[] { "CourtId", "SlotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FacilityHolidays_OwnerId_Date",
                table: "FacilityHolidays",
                columns: new[] { "OwnerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySettings_OwnerId",
                table: "FacilitySettings",
                column: "OwnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySettings_Slug",
                table: "FacilitySettings",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySignups_CourtId_BookingDate_Status",
                table: "OpenPlaySignups",
                columns: new[] { "CourtId", "BookingDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPlaySignups_UserId",
                table: "OpenPlaySignups",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_IsApproved_IsFeatured_DisplayOrder",
                table: "Reviews",
                columns: new[] { "IsApproved", "IsFeatured", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_OwnerId",
                table: "Reviews",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BookingAddOns");

            migrationBuilder.DropTable(
                name: "CourtBlocks");

            migrationBuilder.DropTable(
                name: "CourtBundleCourts");

            migrationBuilder.DropTable(
                name: "CourtBundleRateBlocks");

            migrationBuilder.DropTable(
                name: "CourtRateTiers");

            migrationBuilder.DropTable(
                name: "CourtScheduleBlocks");

            migrationBuilder.DropTable(
                name: "CourtTimeSlots");

            migrationBuilder.DropTable(
                name: "FacilityHolidays");

            migrationBuilder.DropTable(
                name: "FacilitySettings");

            migrationBuilder.DropTable(
                name: "OpenPlaySignups");

            migrationBuilder.DropTable(
                name: "PlatformConfig");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "Sports");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AddOnItems");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "CourtBundles");

            migrationBuilder.DropTable(
                name: "Courts");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
