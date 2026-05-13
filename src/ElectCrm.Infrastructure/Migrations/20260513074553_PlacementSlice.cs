using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlacementSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Placements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacancyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsultantOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Snapshot_PayRate_Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Snapshot_PayRate_Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Snapshot_PayRate_EngagementType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Snapshot_PayRate_HolidayPayInclusive = table.Column<bool>(type: "bit", nullable: false),
                    Snapshot_PayRate_HolidayPayRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SnapshotTakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayRate_Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PayRate_Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PayRate_EngagementType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayRate_HolidayPayInclusive = table.Column<bool>(type: "bit", nullable: false),
                    PayRate_HolidayPayRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    BillRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SnapshotBillRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ProposedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    HoursPerWeek = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Placements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Placements_AgencyBrands_AgencyBrandId",
                        column: x => x.AgencyBrandId,
                        principalTable: "AgencyBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Placements_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Placements_Users_ConsultantOwnerId",
                        column: x => x.ConsultantOwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Placements_Vacancies_VacancyId",
                        column: x => x.VacancyId,
                        principalTable: "Vacancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Placements_AgencyBrandId",
                table: "Placements",
                column: "AgencyBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Placements_AgencyBrandId_ReferenceNumber",
                table: "Placements",
                columns: new[] { "AgencyBrandId", "ReferenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Placements_AgencyBrandId_Status",
                table: "Placements",
                columns: new[] { "AgencyBrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Placements_AgencyBrandId_Status_ProposedStartDate",
                table: "Placements",
                columns: new[] { "AgencyBrandId", "Status", "ProposedStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Placements_CandidateId",
                table: "Placements",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_Placements_CandidateId_Status",
                table: "Placements",
                columns: new[] { "CandidateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Placements_ConsultantOwnerId",
                table: "Placements",
                column: "ConsultantOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Placements_VacancyId",
                table: "Placements",
                column: "VacancyId");

            migrationBuilder.CreateIndex(
                name: "IX_Placements_VacancyId_Status",
                table: "Placements",
                columns: new[] { "VacancyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Placements");
        }
    }
}
