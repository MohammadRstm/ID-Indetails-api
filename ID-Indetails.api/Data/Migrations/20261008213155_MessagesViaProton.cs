using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessagesViaProton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "proton_action_id",
                table: "messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_proton_action_id",
                table: "messages",
                column: "proton_action_id");

            migrationBuilder.AddForeignKey(
                name: "fk_messages_proton_actions_proton_action_id",
                table: "messages",
                column: "proton_action_id",
                principalTable: "proton_actions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_messages_proton_actions_proton_action_id",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "ix_messages_proton_action_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "proton_action_id",
                table: "messages");
        }
    }
}
