using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintServer.Queue.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    origin_directory = table.Column<string>(type: "TEXT", nullable: false),
                    file_name = table.Column<string>(type: "TEXT", nullable: false),
                    source_path = table.Column<string>(type: "TEXT", nullable: false),
                    document_path = table.Column<string>(type: "TEXT", nullable: false),
                    printer_name = table.Column<string>(type: "TEXT", nullable: true),
                    file_created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    file_hash = table.Column<string>(type: "TEXT", nullable: false),
                    file_size = table.Column<long>(type: "INTEGER", nullable: false),
                    source_format = table.Column<string>(type: "TEXT", nullable: false),
                    document_format = table.Column<string>(type: "TEXT", nullable: false),
                    options = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    backend_job_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_blocked_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_origin_file",
                table: "jobs",
                columns: new[] { "origin_directory", "file_name" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_state_next",
                table: "jobs",
                columns: new[] { "state", "next_attempt_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_jobs_active_file",
                table: "jobs",
                columns: new[] { "origin_directory", "file_name", "file_created_at", "file_hash" },
                unique: true,
                filter: "state IN ('in_attesa', 'in_conversione', 'in_stampa')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
