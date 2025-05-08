using Dapper;
using Microsoft.Data.SqlClient;

public static class Database
{
    private static readonly string _connStr =
        "Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;";

    public static void SaveChat(ChatPacket packet)
    {
        using var conn = new SqlConnection(_connStr);
        conn.Execute("""
            INSERT INTO ChatMessages (Sender, Type, Content, FileName, Timestamp)
            VALUES (@Sender, @Type, @Content, @FileName, @Timestamp)
        """, new
        {
            packet.Sender,
            packet.Type,
            packet.Content,
            packet.FileName,
            Timestamp = DateTime.Now
        });
    }
}
