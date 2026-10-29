using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoCapture.Api.Migrations;

[DbContext(typeof(PhotosDb))]
[Migration("202610190001_InitialPhotos")]
public sealed class InitialPhotos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Photos", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Latitude = table.Column<double>(type: "float", nullable: false),
            Longitude = table.Column<double>(type: "float", nullable: false),
            Accuracy = table.Column<double>(type: "float", nullable: false),
            CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            CaptureDate = table.Column<DateOnly>(type: "date", nullable: false),
            CaptureTime = table.Column<TimeOnly>(type: "time", nullable: false),
            ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Photos", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Photos_CaptureDate", table: "Photos", column: "CaptureDate");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "Photos");
}
