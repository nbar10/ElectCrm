using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ChannelPrefs_Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ChannelPrefs_PreferredDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChannelPrefs_QuietHoursStart = table.Column<TimeOnly>(type: "time", nullable: true),
                    ChannelPrefs_QuietHoursEnd = table.Column<TimeOnly>(type: "time", nullable: true),
                    PrimaryForCategories = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_AgencyBrands_AgencyBrandId",
                        column: x => x.AgencyBrandId,
                        principalTable: "AgencyBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_AgencyBrandId",
                table: "Contacts",
                column: "AgencyBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_AgencyBrandId_ClientId",
                table: "Contacts",
                columns: new[] { "AgencyBrandId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_AgencyBrandId_Status",
                table: "Contacts",
                columns: new[] { "AgencyBrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_ClientId",
                table: "Contacts",
                column: "ClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contacts");
        }
    }
}
