using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FullNameNormalised = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    PrimaryPhoneHash = table.Column<string>(type: "nchar(64)", nullable: true),
                    PrimaryPhoneEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NationalInsuranceNumberHash = table.Column<string>(type: "nchar(64)", nullable: true),
                    NationalInsuranceNumberEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassportNumberHash = table.Column<string>(type: "nchar(64)", nullable: true),
                    PassportNumberEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_CreatedAt",
                table: "Persons",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_FullNameNormalised_DateOfBirth",
                table: "Persons",
                columns: new[] { "FullNameNormalised", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_IsDeleted",
                table: "Persons",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_NationalInsuranceNumberHash",
                table: "Persons",
                column: "NationalInsuranceNumberHash");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PassportNumberHash",
                table: "Persons",
                column: "PassportNumberHash");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PrimaryPhoneHash",
                table: "Persons",
                column: "PrimaryPhoneHash");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_Status",
                table: "Persons",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Persons");
        }
    }
}
