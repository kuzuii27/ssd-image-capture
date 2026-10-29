using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoCapture.Api.Migrations;

[DbContext(typeof(PhotosDb))]
[Migration("202610200001_AddNoteClientAndBearerToken")]
public sealed class AddNoteClientAndBearerToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Client", table: "Photos", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Note", table: "Photos", type: "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.CreateTable(name: "BearerTokens", columns: table => new
        {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TokenHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_BearerTokens", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_BearerTokens_TokenHash", table: "BearerTokens", column: "TokenHash", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BearerTokens");
        migrationBuilder.DropColumn(name: "Client", table: "Photos");
        migrationBuilder.DropColumn(name: "Note", table: "Photos");
    }
}
