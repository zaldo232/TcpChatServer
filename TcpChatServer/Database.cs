using Dapper;
using Microsoft.Data.SqlClient;

// 채팅 메시지 DB 저장 유틸리티 클래스
public static class Database
{
    // 데이터베이스 연결 문자열 (MS SQL Server)
    private static readonly string _connStr = "Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;";

    // 채팅 메시지 저장 (DB에 INSERT, 생성된 Id 반환)
    public static int SaveChat(ChatPacket packet)
    {
        using var conn = new SqlConnection(_connStr);   // DB 연결 생성

        // INSERT 쿼리 실행, 생성된 Id 반환
        return conn.ExecuteScalar<int>(
            """
            INSERT INTO ChatMessages (Sender, Receiver, Type, Content, FileName, Timestamp, IsRead, IsDeleted)
            OUTPUT INSERTED.Id
            VALUES (@Sender, @Receiver, @Type, @Content, @FileName, @Timestamp, 0, @IsDeleted)
            """, 
            new
            {
                packet.Sender,     // 보내는 사람
                packet.Receiver,   // 받는 사람
                packet.Type,       // 메시지 타입
                packet.Content,    // 메시지 내용
                packet.FileName,   // 파일명
                packet.Timestamp,  // 보낸 시각
                packet.IsDeleted   // 삭제 여부
            });
    }

}