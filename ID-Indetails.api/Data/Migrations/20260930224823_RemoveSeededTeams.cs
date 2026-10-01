using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// A new company starts without teams: its super admin creates them. The eleven starting teams the
    /// prototype seeded are removed, except any already in use (someone in it, or its group chat), which
    /// the company keeps. Going back puts back the ones that are missing.
    /// </summary>
    public partial class RemoveSeededTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM teams
                WHERE (id, domain_code, name) IN ((1, 'design', 'Engineering'), (2, 'design', 'Design'), (3, 'design', 'Data'), (4, 'design', 'Marketing'), (5, 'design', 'Operations'), (6, 'design', 'Platform'), (7, 'execution', 'Site Operations'), (8, 'execution', 'Procurement'), (9, 'execution', 'Quantity Surveying'), (10, 'execution', 'Safety'), (11, 'execution', 'Project Management'))
                  AND id NOT IN (SELECT team_id FROM users WHERE team_id IS NOT NULL)
                  AND id NOT IN (SELECT team_id FROM conversations WHERE team_id IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT IGNORE INTO teams (id, domain_code, name, created_at) VALUES
                (1, 'design', 'Engineering', '2026-09-28 00:00:00'),
                (2, 'design', 'Design', '2026-09-28 00:00:00'),
                (3, 'design', 'Data', '2026-09-28 00:00:00'),
                (4, 'design', 'Marketing', '2026-09-28 00:00:00'),
                (5, 'design', 'Operations', '2026-09-28 00:00:00'),
                (6, 'design', 'Platform', '2026-09-28 00:00:00'),
                (7, 'execution', 'Site Operations', '2026-09-28 00:00:00'),
                (8, 'execution', 'Procurement', '2026-09-28 00:00:00'),
                (9, 'execution', 'Quantity Surveying', '2026-09-28 00:00:00'),
                (10, 'execution', 'Safety', '2026-09-28 00:00:00'),
                (11, 'execution', 'Project Management', '2026-09-28 00:00:00');
                """);
        }
    }
}
