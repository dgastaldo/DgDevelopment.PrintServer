using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintServer.Queue.Migrations
{
    /// <summary>Renames the stored job states from Italian to English and rebuilds the index that depends on them.</summary>
    [DbContext(typeof(QueueDbContext))]
    [Migration("20261127120000_EnglishStateNames")]
    public partial class EnglishStateNames : Migration
    {
        private static readonly (string Italian, string English)[] States =
        [
            ("in_attesa", "pending"),
            ("in_conversione", "converting"),
            ("in_stampa", "printing"),
            ("completato", "completed"),
            ("fallito", "failed"),
            ("in_stallo", "stalled"),
            ("annullato", "cancelled"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            Rename(migrationBuilder, toEnglish: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            Rename(migrationBuilder, toEnglish: false);

        private static void Rename(MigrationBuilder migrationBuilder, bool toEnglish)
        {
            migrationBuilder.DropIndex(name: "ux_jobs_active_file", table: "jobs");

            foreach (var (italian, english) in States)
            {
                var (from, to) = toEnglish ? (italian, english) : (english, italian);
                migrationBuilder.Sql($"UPDATE jobs SET state = '{to}' WHERE state = '{from}';");
            }

            migrationBuilder.CreateIndex(
                name: "ux_jobs_active_file",
                table: "jobs",
                columns: new[] { "origin_directory", "file_name", "file_created_at", "file_hash" },
                unique: true,
                filter: toEnglish
                    ? "state IN ('pending', 'converting', 'printing')"
                    : "state IN ('in_attesa', 'in_conversione', 'in_stampa')");
        }
    }
}
