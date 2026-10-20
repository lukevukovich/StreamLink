using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamLink.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ShareLinks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ShareLinks" PRIMARY KEY AUTOINCREMENT,
                    "Token" TEXT NOT NULL,
                    "ChannelName" TEXT NOT NULL,
                    "CategoryName" TEXT NOT NULL,
                    "StreamId" INTEGER NOT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    "ExpiresUtc" TEXT NOT NULL,
                    "Revoked" INTEGER NOT NULL,
                    "OwnerKey" TEXT NOT NULL,
                    "ProtectedHlsUrl" TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS "ThemePreferences" (
                    "OwnerKey" TEXT NOT NULL CONSTRAINT "PK_ThemePreferences" PRIMARY KEY,
                    "Theme" TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ShareLinks_Token"
                    ON "ShareLinks" ("Token");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "ShareLinks";
                DROP TABLE IF EXISTS "ThemePreferences";
                """);
        }
    }
}
