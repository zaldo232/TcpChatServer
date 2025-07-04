using Dapper;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// 서버 시작 메시지 DB에 저장
Database.SaveChat(new ChatPacket { Type = "system", Sender = "서버", Content = "서버 시작됨" });

// TCP 리스너 생성 및 포트 9000에서 대기
TcpListener listener = new TcpListener(IPAddress.Any, 9000);
listener.Start();
Console.WriteLine("서버 실행 중...");

// 접속한 유저 목록 (닉네임, TcpClient) 관리
ConcurrentDictionary<string, TcpClient> connectedUsers = new();

// 클라이언트 연결 수신 루프 (비동기)
_ = Task.Run(async () =>
{
    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();   // 새 클라이언트 수신
        _ = HandleClientAsync(client);                       // 처리 코루틴 시작
    }
});

// 클라이언트별 패킷 처리 메인 함수
async Task HandleClientAsync(TcpClient client)
{
    using var stream = client.GetStream();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    string? username = null;

    try
    {
        while (true)
        {
            // 클라이언트 패킷 수신
            string? json = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(json)) break;

            var packet = JsonSerializer.Deserialize<ChatPacket>(json);
            if (packet == null) continue;

            // 핑/퐁 처리 (연결 유지)
            if (packet.Type == "ping")
            {
                await SendPacketTo(client, new ChatPacket
                {
                    Type = "pong",
                    Sender = "서버",
                    Receiver = packet.Sender,
                    Timestamp = DateTime.Now
                });
                continue;
            }

            // 타이핑 표시 전달
            if (packet.Type == "typing")
            {
                if (!string.IsNullOrEmpty(packet.Receiver) && connectedUsers.TryGetValue(packet.Receiver, out var target))
                {
                    await SendPacketTo(target, packet);
                }
                continue;
            }

            // 암호화 메시지 복호화 및 저장/전달
            if (packet.Type == "message" && !string.IsNullOrEmpty(packet.Content))
            {
                string encryptedContent = packet.Content;
                string decryptedContent;

                try
                {
                    Debug.WriteLine($"[복호화 전 암호문] {encryptedContent}");
                    decryptedContent = AesEncryption.Decrypt(encryptedContent);
                    Debug.WriteLine($"[복호화 결과] {decryptedContent}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[복호화 실패] {ex.Message}");
                    decryptedContent = "[복호화 실패]";
                }

                // 암호문 상태 그대로 DB에 저장
                var savePacket = new ChatPacket
                {
                    Type = packet.Type,
                    Sender = packet.Sender,
                    Receiver = packet.Receiver,
                    Content = encryptedContent,
                    FileName = packet.FileName,
                    Timestamp = packet.Timestamp,
                    IsRead = packet.IsRead,
                    IsDeleted = packet.IsDeleted
                };

                savePacket.Id = Database.SaveChat(savePacket);

                // 클라이언트에게 보낼 때는 복호화된 내용을 보냄
                var displayPacket = new ChatPacket
                {
                    Id = savePacket.Id,
                    Type = savePacket.Type,
                    Sender = savePacket.Sender,
                    Receiver = savePacket.Receiver,
                    Content = decryptedContent,
                    FileName = savePacket.FileName,
                    Timestamp = savePacket.Timestamp,
                    IsRead = savePacket.IsRead,
                    IsDeleted = savePacket.IsDeleted
                };

                // 단일 or 전체 대상 전송
                if (!string.IsNullOrEmpty(displayPacket.Receiver))
                {
                    if (connectedUsers.TryGetValue(displayPacket.Receiver, out var targetClient))
                    { 
                        await SendPacketTo(targetClient, displayPacket); 
                    }

                    if (connectedUsers.TryGetValue(displayPacket.Sender, out var senderClient))
                    { 
                        await SendPacketTo(senderClient, displayPacket); 
                    }
                }
                else
                {
                    foreach (var (name, tcp) in connectedUsers)
                    {
                        if (name != packet.Sender)
                        { 
                            await SendPacketTo(tcp, displayPacket); 
                        }
                    }
                }

                continue;
            }

            // 읽음 표시 처리
            if (packet.Type == "mark_read")
            {
                MarkMessagesAsRead(packet.Sender, packet.Receiver); // DB 업데이트

                // 읽음 알림 패킷 생성
                var notify = new ChatPacket
                {
                    Type = "read_notify",
                    Sender = packet.Sender,     // 읽은 사람
                    Receiver = packet.Receiver  // 원래 보낸 사람
                };

                if (connectedUsers.TryGetValue(packet.Receiver, out var targetClient))  // 이걸로 바꿔야 보낸 애한테 감
                {
                    await SendPacketTo(targetClient, notify);
                }

                await SendAllUsersPacket(packet.Sender);
                await BroadcastUserList();
                continue;
            }

            // 파일 다운로드 요청 처리
            if (packet.Type == "download")
            {
                try
                {
                    string path = packet.Content;
                    byte[] data = File.ReadAllBytes(path);
                    byte[] encrypted = AesEncryption.EncryptBytes(data);
                    string base64 = Convert.ToBase64String(encrypted);

                    var response = new ChatPacket
                    {
                        Type = "download_result",
                        Sender = "서버",
                        Receiver = packet.Sender,
                        Content = base64,
                        FileName = packet.FileName,
                        Timestamp = DateTime.Now
                    };

                    if (connectedUsers.TryGetValue(packet.Sender, out var targetClient))
                    {
                        await SendPacketTo(targetClient, response);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[다운로드 오류] {ex.Message}");
                }
                continue;
            }

            // 파일 업로드/전송 처리
            if (packet.Type == "file")
            {
                string saveDir = Path.Combine("ChatFiles");
                Directory.CreateDirectory(saveDir);

                string newFileName = $"{Guid.NewGuid()}_{packet.FileName}";
                string fullPath = Path.Combine(saveDir, newFileName);

                try
                {
                    // 복호화하여 저장
                    byte[] encrypted = Convert.FromBase64String(packet.Content);
                    byte[] decrypted = AesEncryption.DecryptBytes(encrypted);
                    File.WriteAllBytes(fullPath, decrypted);

                    // 다시 읽어서 암호화 후 Base64로 전송
                    byte[] rawBytes = File.ReadAllBytes(fullPath);
                    byte[] reEncrypted = AesEncryption.EncryptBytes(rawBytes);
                    string base64 = Convert.ToBase64String(reEncrypted);


                    packet.Content = base64;
                    packet.FileName = newFileName;

                    Database.SaveChat(packet);

                    // 상대방에게 전송
                    if (connectedUsers.TryGetValue(packet.Receiver, out var targetClient))
                    { 
                        await SendPacketTo(targetClient, packet); 
                    }

                    // 본인에게도 전송
                    if (connectedUsers.TryGetValue(packet.Sender, out var senderClient))
                    { 
                        await SendPacketTo(senderClient, packet); 
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[파일 저장 오류] {ex.Message}");
                }

                continue;
            }

            // 대화 이력 요청 처리
            if (packet.Type == "get_history")
            {
                var history = GetChatHistory(packet.Sender, packet.Receiver);

                // 메시지 복호화 처리
                foreach (var msg in history)
                {
                    if (msg.Type == "message" && !string.IsNullOrEmpty(msg.Content))
                    {
                        try
                        {
                            msg.Content = AesEncryption.Decrypt(msg.Content);
                        }
                        catch
                        {
                            msg.Content = "[복호화 실패]";
                        }
                    }
                }

                var historyPacket = new ChatPacket
                {
                    Type = "history",
                    Sender = "서버",
                    Content = JsonSerializer.Serialize(history)
                };

                await SendPacketTo(client, historyPacket);
                continue;
            }

            // 메시지 삭제 처리
            if (packet.Type == "delete")
            {
                try
                {
                    // 삭제 요청 처리
                    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
                    conn.Execute("""
                            UPDATE ChatMessages
                            SET IsDeleted = 1, Content = '삭제된 메시지입니다'
                            WHERE Id = @Id
                            """, new { packet.Id });

                    // 삭제한 사용자에겐 삭제 표시
                    var toSender = new ChatPacket
                    {
                        Type = "delete_notify",
                        Sender = packet.Sender,
                        Receiver = packet.Receiver,
                        Timestamp = packet.Timestamp,
                        Content = "삭제된 메시지입니다",
                        IsDeleted = true,
                        Id = packet.Id
                    };

                    // 상대방에겐 내용은 그대로, IsDeleted = false
                    var toReceiver = new ChatPacket
                    {
                        Type = "delete_notify",
                        Sender = packet.Sender,
                        Receiver = packet.Receiver,
                        Timestamp = packet.Timestamp,
                        Content = packet.Content,  // 원래 내용
                        IsDeleted = false,
                        Id = packet.Id
                    };

                    if (connectedUsers.TryGetValue(packet.Sender, out var senderClient))
                    { 
                        await SendPacketTo(senderClient, toSender); 
                    }

                    if (connectedUsers.TryGetValue(packet.Receiver, out var receiverClient))
                    { 
                        await SendPacketTo(receiverClient, toReceiver); 
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[삭제 처리 오류] {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }

                continue;
            }

            // 최초 접속 처리
            if (username == null)
            {
                username = packet.Sender;
                connectedUsers[username] = client;
                Console.WriteLine($"[접속] {username}");

                await SendAllUsersPacket(username); // 내게만 전체유저 목록
                await BroadcastUserList();         // 전체 유저에 리스트 전송
            }

            // 그 외 메시지 DB 저장 및 전송
            packet.Id = Database.SaveChat(packet);

            if (!string.IsNullOrEmpty(packet.Receiver))
            {
                if (connectedUsers.TryGetValue(packet.Receiver, out var targetClient))
                {
                    await SendPacketTo(targetClient, packet); // 상대방에게 보냄
                }

                if (connectedUsers.TryGetValue(packet.Sender, out var senderClient))
                {
                    await SendPacketTo(senderClient, packet); // 본인에게도 보냄 (내 화면에 뜨게)
                }
            }
            else
            {
                foreach (var (name, tcp) in connectedUsers)
                {
                    if (name != packet.Sender)
                    {
                        await SendPacketTo(tcp, packet);
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[에러] {username}: {ex.Message}");
    }
    finally
    {
        if (username != null)
        {
            connectedUsers.TryRemove(username, out _);
            Console.WriteLine($"[종료] {username}");
            await BroadcastUserList();
        }
        client.Close();
    }
}

// (아래부터는 서버에서 사용되는 헬퍼 함수들, 각 함수 위/내부 주석)
async Task SendAllUsersPacket(string username)
{
    // 전체 유저리스트+안읽은 메시지 개수 반환
    var allUserList = GetAllUsernamesFromDatabase();
    var unreadCounts = GetUnreadMessageCounts(username);

    var userListWithUnread = allUserList
        .Where(u => u != username)
        .Select(u => unreadCounts.ContainsKey(u) ? $"{u} ({unreadCounts[u]})" : u)
        .ToList();

    if (connectedUsers.TryGetValue(username, out var client))
    {
        var packet = new ChatPacket
        {
            Type = "allusers",
            Content = string.Join(",", userListWithUnread),
            Sender = "서버"
        };
        await SendPacketTo(client, packet);
    }
}

// 전체 접속자에게 유저리스트 브로드캐스트
async Task BroadcastUserList()
{
    var userlist = string.Join(",", connectedUsers.Keys);
    var packet = new ChatPacket
    {
        Type = "userlist",
        Content = userlist,
        Sender = "서버",
        Timestamp = DateTime.Now
    };
    await BroadcastPacket(packet);
}

// 패킷 전체 유저에게 브로드캐스트
async Task BroadcastPacket(ChatPacket packet)
{
    string json = JsonSerializer.Serialize(packet) + "\n";
    byte[] data = Encoding.UTF8.GetBytes(json);

    foreach (var (_, client) in connectedUsers)
    {
        try
        {
            var stream = client.GetStream();
            await stream.WriteAsync(data, 0, data.Length);
        }
        catch { }
    }
}

// 특정 클라이언트에 패킷 전송
async Task SendPacketTo(TcpClient client, ChatPacket packet)
{
    var stream = client.GetStream();
    var json = JsonSerializer.Serialize(packet) + "\n";
    byte[] data = Encoding.UTF8.GetBytes(json);
    await stream.WriteAsync(data, 0, data.Length);
}

// DB에서 모든 유저명 조회
List<string> GetAllUsernamesFromDatabase()
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    return conn.Query<string>("SELECT Username FROM Users").ToList();
}

// DB에서 안읽은 메시지 개수 조회
Dictionary<string, int> GetUnreadMessageCounts(string receiver)
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    var result = conn.Query<(string Sender, int Count)>(
        """
        SELECT Sender, COUNT(*) AS Count
        FROM ChatMessages
        WHERE Receiver = @Receiver AND IsRead = 0
        GROUP BY Sender
        """, new { Receiver = receiver });

    return result.ToDictionary(x => x.Sender, x => x.Count);
}

// 특정 송수신자 쌍의 메시지 전체 읽음 처리
void MarkMessagesAsRead(string receiver, string sender)
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    conn.Execute(
        @"UPDATE ChatMessages
          SET IsRead = 1
          WHERE Receiver = @Receiver AND Sender = @Sender AND IsRead = 0",
        new { Receiver = receiver, Sender = sender });
}

// 두 유저 간의 전체 채팅 내역 조회
List<ChatPacket> GetChatHistory(string sender, string receiver)
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    return conn.Query<ChatPacket>(
            @"SELECT Id, Sender, Receiver, Content, FileName, Type, Timestamp, IsRead, IsDeleted
            FROM ChatMessages
            WHERE (Sender = @A AND Receiver = @B) OR (Sender = @B AND Receiver = @A)
            ORDER BY Timestamp",
    new { A = sender, B = receiver }
    ).ToList();
}

Console.ReadLine(); // 서버 종료 방지