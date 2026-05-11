using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegistrationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OwnerConsultantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrimaryTrade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceLegacyId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidates_AgencyBrands_AgencyBrandId",
                        column: x => x.AgencyBrandId,
                        principalTable: "AgencyBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Candidates_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Candidates_Users_OwnerConsultantId",
                        column: x => x.OwnerConsultantId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_AgencyBrandId",
                table: "Candidates",
                column: "AgencyBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_AgencyBrandId_Status",
                table: "Candidates",
                columns: new[] { "AgencyBrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_IsDeleted",
                table: "Candidates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_OwnerConsultantId",
                table: "Candidates",
                column: "OwnerConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_PersonId",
                table: "Candidates",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_PersonId_AgencyBrandId",
                table: "Candidates",
                columns: new[] { "PersonId", "AgencyBrandId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_RegistrationDate",
                table: "Candidates",
                column: "RegistrationDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Candidates");
        }
    }
}
