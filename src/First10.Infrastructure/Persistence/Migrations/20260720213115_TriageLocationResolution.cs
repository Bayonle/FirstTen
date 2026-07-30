using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TriageLocationResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "LocationConfidence",
                schema: "intake",
                table: "triage_assessments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationEvidenceReference",
                schema: "intake",
                table: "triage_assessments",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LocationFromPin",
                schema: "intake",
                table: "triage_assessments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedDirection",
                schema: "intake",
                table: "triage_assessments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "ResolvedLandmarkId",
                schema: "intake",
                table: "triage_assessments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ResolvedLatitude",
                schema: "intake",
                table: "triage_assessments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ResolvedLongitude",
                schema: "intake",
                table: "triage_assessments",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationConfidence",
                schema: "intake",
                table: "triage_assessments");

            migrationBuilder.DropColumn(
                name: "LocationEvidenceReference",
                schema: "intake",
                table: "triage_assessments");

            migrationBuilder.DropColumn(
                name: "LocationFromPin",
                schema: "intake",
                table: "triage_assessments");

            migrationBuilder.DropColumn(
                name: "ResolvedDirection",
                schema: "intake",
                table: "triage_assessments");

            migrationBuilder.DropColumn(
                name: "ResolvedLandmarkId",
                schema: "intake",
                table: "triage_assessments");

            migrationBuilder.DropColumn(
                name: "ResolvedLatitude",
                schema: "intake",
                table: "triage_assessments");

            migrationBuilder.DropColumn(
                name: "ResolvedLongitude",
                schema: "intake",
                table: "triage_assessments");
        }
    }
}
