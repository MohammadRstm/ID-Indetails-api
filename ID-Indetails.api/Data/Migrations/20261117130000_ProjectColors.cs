using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Every project without a colour gets one of <see cref="Business.Projects.ProjectColorRules.Palette"/>: the active
    /// and on-hold projects first, in turn, so the first sixteen differ. The order spreads the hues, so a few projects
    /// already look apart. Colours projects have are kept. No column changes.
    /// </summary>
    public partial class ProjectColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `projects` p
                JOIN (
                    SELECT `id`, ROW_NUMBER() OVER (ORDER BY `status` IN ('active', 'hold') DESC, `id`) - 1 AS `n`
                    FROM `projects`
                    WHERE `tint` IS NULL OR TRIM(`tint`) = ''
                ) x ON x.`id` = p.`id`
                SET p.`tint` = ELT(MOD(x.`n`, 16) + 1,
                    '#2F6FEB', '#D9730D', '#2F9E44', '#C026A3', '#0E8A84', '#B7791F', '#8B5CF6', '#E0603F',
                    '#0B8DB8', '#7C8A16', '#D6336C', '#5F7085', '#5B5BD6', '#0F9F75', '#9C6644', '#9D3FC7');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the colours stay: those given here can't be told from those people chose since
        }
    }
}
