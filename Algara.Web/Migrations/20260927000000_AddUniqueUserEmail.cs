using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    public partial class AddUniqueUserEmail : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The migration transaction holds the table lock through index creation.
            // Existing accounts must be reviewed explicitly, never merged or truncated.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [Users] WITH (TABLOCKX, HOLDLOCK)
                    WHERE [Email] IS NULL OR LTRIM(RTRIM([Email])) = N'')
                    THROW 51000, 'Cannot enforce unique email: Users contains missing or blank emails. Review existing accounts before retrying.', 1;

                IF EXISTS (SELECT 1 FROM [Users] WHERE DATALENGTH([Email]) > 512)
                    THROW 51001, 'Cannot enforce unique email: Users contains emails longer than 256 UTF-16 code units. Review existing accounts before retrying.', 1;

                IF EXISTS (SELECT [Email] FROM [Users] GROUP BY [Email] HAVING COUNT_BIG(*) > 1)
                    THROW 51002, 'Cannot enforce unique email: Users contains duplicate emails under the existing column collation. Review existing accounts before retrying.', 1;

                DECLARE @emailCollation sysname = (
                    SELECT [collation_name] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'[Users]') AND [name] = N'Email');
                IF @emailCollation IS NULL
                    THROW 51003, 'Cannot determine the existing Users.Email collation.', 1;

                DECLARE @alterEmail nvarchar(max) = N'ALTER TABLE [Users] ALTER COLUMN [Email] nvarchar(256) COLLATE '
                    + @emailCollation + N' NOT NULL;';
                EXEC sys.sp_executesql @alterEmail;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_Email", table: "Users");

            migrationBuilder.Sql("""
                DECLARE @emailCollation sysname = (
                    SELECT [collation_name] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'[Users]') AND [name] = N'Email');
                IF @emailCollation IS NULL
                    THROW 51003, 'Cannot determine the existing Users.Email collation.', 1;

                DECLARE @alterEmail nvarchar(max) = N'ALTER TABLE [Users] ALTER COLUMN [Email] nvarchar(max) COLLATE '
                    + @emailCollation + N' NOT NULL;';
                EXEC sys.sp_executesql @alterEmail;
                """);
        }
    }
}
