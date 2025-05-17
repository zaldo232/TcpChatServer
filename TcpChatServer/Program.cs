using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Dapper;

Database.SaveChat(new ChatPacket { Type = "system", Sender = "서버", Content = "서버 시작됨" });

TcpListener listener = new TcpListener(IPAddress.Any, 9000);
listener.Start();
Console.WriteLine("서버 실행 중...");

ConcurrentDictionary<string, TcpClient> connectedUsers = new();

_ = Task.Run(async () =>
{
    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        _ = HandleClientAsync(client);
    }
});

async Task HandleClientAsync(TcpClient client)
{
    using var stream = client.GetStream();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    string? username = null;

    try
    {
        while (true)
        {
            string? json = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(json)) break;

            var packet = JsonSerializer.Deserialize<ChatPacket>(json);
            if (packet == null) continue;

            if (packet.Type == "get_history")
            {
                var history = GetChatHistory(packet.Sender, packet.Receiver);
                var historyPacket = new ChatPacket
                {
                    Type = "history",
                    Sender = "서버",
                    Content = JsonSerializer.Serialize(history)
                };
                await SendPacketTo(client, historyPacket);
                continue;
            }

            if (packet.Type == "mark_read")
            {
                MarkMessagesAsRead(packet.Sender, packet.Receiver);

                // 읽음 통보 패킷 보내기
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

            if (packet.Type == "download")
            {
                try
                {
                    string path = packet.Content;
                    byte[] data = File.ReadAllBytes(path);
                    string base64 = Convert.ToBase64String(data);

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

            if (username == null)
            {
                username = packet.Sender;
                connectedUsers[username] = client;
                Console.WriteLine($"[접속] {username}");

                await SendAllUsersPacket(username);
                await BroadcastUserList();
            }

            if (packet.Type == "file")
            {
                string saveDir = Path.Combine("ChatFiles");
                Directory.CreateDirectory(saveDir);

                string newFileName = $"{Guid.NewGuid()}_{packet.FileName}";
                string fullPath = Path.Combine(saveDir, newFileName);

                try
                {
                    // 클라이언트가 보낸 Base64 디코드해서 파일로 저장
                    byte[] fileBytes = Convert.FromBase64String(packet.Content);
                    File.WriteAllBytes(fullPath, fileBytes);

                    // 서버에서 다시 읽어서 Base64로 변환해서 전송
                    byte[] rawBytes = File.ReadAllBytes(fullPath);
                    string base64 = Convert.ToBase64String(rawBytes);

                    packet.Content = base64;
                    packet.FileName = newFileName;

                    Database.SaveChat(packet);

                    // 상대방에게 전송
                    if (connectedUsers.TryGetValue(packet.Receiver, out var targetClient))
                        await SendPacketTo(targetClient, packet);

                    // 본인에게도 전송
                    if (connectedUsers.TryGetValue(packet.Sender, out var senderClient))
                        await SendPacketTo(senderClient, packet);

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[파일 저장 오류] {ex.Message}");
                }

                continue;
            }


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
                        await SendPacketTo(senderClient, toSender);

                    if (connectedUsers.TryGetValue(packet.Receiver, out var receiverClient))
                        await SendPacketTo(receiverClient, toReceiver);

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[삭제 처리 오류] {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }

                continue;
            }


            packet.Id = Database.SaveChat(packet);

            if (!string.IsNullOrEmpty(packet.Receiver))
            {
                if (connectedUsers.TryGetValue(packet.Receiver, out var targetClient))
                {
                    await SendPacketTo(targetClient, packet); // 상대방에게 보냄
                }

                if (connectedUsers.TryGetValue(packet.Sender, out var senderClient))
                {
                    await SendPacketTo(senderClient, packet); // 본인에게도 보냄 (내 화면에 뜨게!)
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

async Task SendAllUsersPacket(string username)
{
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

async Task SendPacketTo(TcpClient client, ChatPacket packet)
{
    var stream = client.GetStream();
    var json = JsonSerializer.Serialize(packet) + "\n";
    byte[] data = Encoding.UTF8.GetBytes(json);
    await stream.WriteAsync(data, 0, data.Length);
}

List<string> GetAllUsernamesFromDatabase()
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    return conn.Query<string>("SELECT Username FROM Users").ToList();
}

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

void MarkMessagesAsRead(string receiver, string sender)
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    conn.Execute(
        @"UPDATE ChatMessages
          SET IsRead = 1
          WHERE Receiver = @Receiver AND Sender = @Sender AND IsRead = 0",
        new { Receiver = receiver, Sender = sender });
}

List<ChatPacket> GetChatHistory(string sender, string receiver)
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    return conn.Query<ChatPacket>(
        @"SELECT Sender, Receiver, Content, FileName, Type, Timestamp, IsRead, IsDeleted
        FROM ChatMessages
        WHERE (Sender = @A AND Receiver = @B) OR (Sender = @B AND Receiver = @A)
        ORDER BY Timestamp",
        new { A = sender, B = receiver }
    ).ToList();
}

Console.ReadLine();
