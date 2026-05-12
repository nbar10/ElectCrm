using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVacancies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrimaryBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TradingName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clients_AgencyBrands_AgencyBrandId",
                        column: x => x.AgencyBrandId,
                        principalTable: "AgencyBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clients_Branches_PrimaryBranchId",
                        column: x => x.PrimaryBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vacancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsultantOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RoleTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true),
                    LocationPostcode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ShiftPattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayRate_Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PayRate_Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PayRate_EngagementType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayRate_HolidayPayInclusive = table.Column<bool>(type: "bit", nullable: false),
                    PayRate_HolidayPayRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    BillRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    HeadcountRequired = table.Column<int>(type: "int", nullable: false),
                    RequiredCards = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedFrom = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vacancies_AgencyBrands_AgencyBrandId",
                        column: x => x.AgencyBrandId,
                        principalTable: "AgencyBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vacancies_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vacancies_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vacancies_Users_ConsultantOwnerId",
                        column: x => x.ConsultantOwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_AgencyBrandId",
                table: "Clients",
                column: "AgencyBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_AgencyBrandId_Status",
                table: "Clients",
                columns: new[] { "AgencyBrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_IsDeleted",
                table: "Clients",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_PrimaryBranchId",
                table: "Clients",
                column: "PrimaryBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_AgencyBrandId",
                table: "Vacancies",
                column: "AgencyBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_AgencyBrandId_ReferenceNumber",
                table: "Vacancies",
                columns: new[] { "AgencyBrandId", "ReferenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_AgencyBrandId_Status",
                table: "Vacancies",
                columns: new[] { "AgencyBrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_BranchId",
                table: "Vacancies",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_ClientId",
                table: "Vacancies",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_ConsultantOwnerId",
                table: "Vacancies",
                column: "ConsultantOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_StartDate",
                table: "Vacancies",
                column: "StartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Vacancies");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
