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
            INSERT INTO ChatMessages (Sender, Receiver, Type, Content, FileName, Timestamp, IsRead)
            VALUES (@Sender, @Receiver, @Type, @Content, @FileName, @Timestamp, 0)
        """, new
        {
            packet.Sender,
            packet.Receiver,
            packet.Type,
            packet.Content,
            packet.FileName,
            packet.Timestamp
        });
    }
}