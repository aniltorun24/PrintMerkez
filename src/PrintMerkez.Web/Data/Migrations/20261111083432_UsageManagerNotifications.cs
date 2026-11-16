using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintMerkez.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsageManagerNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DirectoryUserId",
                table: "Jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedAccount",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "DirectoryUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "DirectoryUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ManagerAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ManagerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagerAssignments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManagerAssignments_DirectoryUsers_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalTable: "DirectoryUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManagerAssignments_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsageAlertPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Threshold = table.Column<int>(type: "INTEGER", nullable: false),
                    Period = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageAlertPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PolicyId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodLabel = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalImpressions = table.Column<long>(type: "INTEGER", nullable: false),
                    Threshold = table.Column<int>(type: "INTEGER", nullable: false),
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    DepartmentName = table.Column<string>(type: "TEXT", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientEmail = table.Column<string>(type: "TEXT", nullable: true),
                    ManagerUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClaimToken = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageNotifications_DirectoryUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "DirectoryUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsageNotifications_UsageAlertPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "UsageAlertPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_DirectoryUserId",
                table: "Jobs",
                column: "DirectoryUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_NormalizedAccount_Source_OccurredAtUtc",
                table: "Jobs",
                columns: new[] { "NormalizedAccount", "Source", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryUsers_LocationId",
                table: "DirectoryUsers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagerAssignments_DepartmentId_LocationId",
                table: "ManagerAssignments",
                columns: new[] { "DepartmentId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagerAssignments_LocationId",
                table: "ManagerAssignments",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagerAssignments_ManagerUserId",
                table: "ManagerAssignments",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAlertPolicies_Name",
                table: "UsageAlertPolicies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageNotifications_PolicyId_UserId_PeriodKey_Source",
                table: "UsageNotifications",
                columns: new[] { "PolicyId", "UserId", "PeriodKey", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageNotifications_State_NextAttemptAtUtc",
                table: "UsageNotifications",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageNotifications_UserId",
                table: "UsageNotifications",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectoryUsers_Locations_LocationId",
                table: "DirectoryUsers",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_DirectoryUsers_DirectoryUserId",
                table: "Jobs",
                column: "DirectoryUserId",
                principalTable: "DirectoryUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectoryUsers_Locations_LocationId",
                table: "DirectoryUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_DirectoryUsers_DirectoryUserId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "ManagerAssignments");

            migrationBuilder.DropTable(
                name: "UsageNotifications");

            migrationBuilder.DropTable(
                name: "UsageAlertPolicies");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_DirectoryUserId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_NormalizedAccount_Source_OccurredAtUtc",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_DirectoryUsers_LocationId",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "DirectoryUserId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "NormalizedAccount",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "DirectoryUsers");
        }
    }
}
