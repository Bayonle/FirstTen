using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationsPilotGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.CreateTable(
                name: "pilot_gate_evidence",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pilot_gate_evidence", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pilot_gate_evidence_Type_RecordedAtUtc",
                schema: "operations",
                table: "pilot_gate_evidence",
                columns: new[] { "Type", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pilot_gate_evidence",
                schema: "operations");
        }
    }
}
