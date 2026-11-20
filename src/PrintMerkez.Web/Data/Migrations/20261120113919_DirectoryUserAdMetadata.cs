using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintMerkez.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DirectoryUserAdMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdDepartment",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdDistinguishedName",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdDomain",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdImportedAtUtc",
                table: "DirectoryUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdObjectGuid",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdOffice",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdSamAccountName",
                table: "DirectoryUsers",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryUsers_AdDomain_AdSamAccountName",
                table: "DirectoryUsers",
                columns: new[] { "AdDomain", "AdSamAccountName" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryUsers_AdObjectGuid",
                table: "DirectoryUsers",
                column: "AdObjectGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DirectoryUsers_AdDomain_AdSamAccountName",
                table: "DirectoryUsers");

            migrationBuilder.DropIndex(
                name: "IX_DirectoryUsers_AdObjectGuid",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdDepartment",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdDistinguishedName",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdDomain",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdImportedAtUtc",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdObjectGuid",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdOffice",
                table: "DirectoryUsers");

            migrationBuilder.DropColumn(
                name: "AdSamAccountName",
                table: "DirectoryUsers");
        }
    }
}
