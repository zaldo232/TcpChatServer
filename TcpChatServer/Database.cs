using Dapper;
using Microsoft.Data.SqlClient;

public static class Database
{
    private static readonly string _connStr =
        "Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;";

    public static int SaveChat(ChatPacket packet)
    {
        using var conn = new SqlConnection(_connStr);

        return conn.ExecuteScalar<int>(
            """
        INSERT INTO ChatMessages (Sender, Receiver, Type, Content, FileName, Timestamp, IsRead, IsDeleted)
        OUTPUT INSERTED.Id
        VALUES (@Sender, @Receiver, @Type, @Content, @FileName, @Timestamp, 0, @IsDeleted)
        """, new
            {
                packet.Sender,
                packet.Receiver,
                packet.Type,
                packet.Content,
                packet.FileName,
                packet.Timestamp,
                packet.IsDeleted
            });
    }

}