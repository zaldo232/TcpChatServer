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
                await SendAllUsersPacket(packet.Sender);   // ✅ (N) 반영
                await BroadcastUserList();                // ✅ 접속중 유저 반영
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

            Console.WriteLine($"[{packet.Sender} → {packet.Receiver ?? "전체"}] {packet.Type}: {packet.Content}");
            Database.SaveChat(packet);

            if (packet.Type == "file")
            {
                string saveDir = Path.Combine("ReceivedFiles");
                Directory.CreateDirectory(saveDir);
                string path = Path.Combine(saveDir, packet.FileName);
                File.WriteAllBytes(path, Convert.FromBase64String(packet.Content));
                Console.WriteLine($"파일 저장됨: {path}");
            }

            if (!string.IsNullOrEmpty(packet.Receiver))
            {
                if (connectedUsers.TryGetValue(packet.Receiver, out var targetClient))
                {
                    var targetStream = targetClient.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                    await targetStream.WriteAsync(data, 0, data.Length);
                }
            }
            else
            {
                foreach (var (name, tcp) in connectedUsers)
                {
                    if (name != packet.Sender)
                    {
                        var s = tcp.GetStream();
                        byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                        await s.WriteAsync(data, 0, data.Length);
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
    string json = JsonSerializer.Serialize(packet) + "\n";
    byte[] data = Encoding.UTF8.GetBytes(json);

    Console.WriteLine($"[브로드캐스트] userlist → {userlist}");

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

async Task SendPacketTo(TcpClient client, ChatPacket packet)
{
    var stream = client.GetStream();
    var json = JsonSerializer.Serialize(packet) + "\n";
    byte[] data = Encoding.UTF8.GetBytes(json);
    await stream.WriteAsync(data, 0, data.Length);
}

List<ChatPacket> GetChatHistory(string sender, string receiver)
{
    using var conn = new SqlConnection("Server=localhost;Database=ChatServerDb;User Id=sa;Password=1234;TrustServerCertificate=True;");
    return conn.Query<ChatPacket>(
        @"SELECT Sender, Receiver, Content, FileName, Type, Timestamp
          FROM ChatMessages
          WHERE (Sender = @A AND Receiver = @B) OR (Sender = @B AND Receiver = @A)
          ORDER BY Timestamp",
        new { A = sender, B = receiver }
    ).ToList();
}

Console.ReadLine();
