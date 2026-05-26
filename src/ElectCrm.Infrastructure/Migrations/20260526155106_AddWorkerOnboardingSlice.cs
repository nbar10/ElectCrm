using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerOnboardingSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplianceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OtherDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastModifiedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceDocuments_AspNetUsers_LastModifiedById",
                        column: x => x.LastModifiedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComplianceDocuments_AspNetUsers_VerifiedByUserId",
                        column: x => x.VerifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ComplianceDocuments_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkerInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByConsultantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Token = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PrefillFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PrefillLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PrefillEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PrefillPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExistingPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedByPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkerInvites_AgencyBrands_AgencyBrandId",
                        column: x => x.AgencyBrandId,
                        principalTable: "AgencyBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerInvites_AspNetUsers_CreatedByConsultantId",
                        column: x => x.CreatedByConsultantId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkerInvites_Persons_ConsumedByPersonId",
                        column: x => x.ConsumedByPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkerInvites_Persons_ExistingPersonId",
                        column: x => x.ExistingPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    NationalInsuranceNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Postcode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RightToWorkDeclaredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkerProfiles_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceDocuments_ExpiryDate",
                table: "ComplianceDocuments",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceDocuments_LastModifiedById",
                table: "ComplianceDocuments",
                column: "LastModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceDocuments_PersonId",
                table: "ComplianceDocuments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceDocuments_PersonId_DocumentType",
                table: "ComplianceDocuments",
                columns: new[] { "PersonId", "DocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceDocuments_Status",
                table: "ComplianceDocuments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceDocuments_VerifiedByUserId",
                table: "ComplianceDocuments",
                column: "VerifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerInvites_AgencyBrandId_Status",
                table: "WorkerInvites",
                columns: new[] { "AgencyBrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerInvites_ConsumedByPersonId",
                table: "WorkerInvites",
                column: "ConsumedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerInvites_CreatedByConsultantId",
                table: "WorkerInvites",
                column: "CreatedByConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerInvites_ExistingPersonId",
                table: "WorkerInvites",
                column: "ExistingPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerInvites_ExpiresAt",
                table: "WorkerInvites",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerInvites_Token",
                table: "WorkerInvites",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkerProfiles_ApplicationUserId",
                table: "WorkerProfiles",
                column: "ApplicationUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplianceDocuments");

            migrationBuilder.DropTable(
                name: "WorkerInvites");

            migrationBuilder.DropTable(
                name: "WorkerProfiles");
        }
    }
}
