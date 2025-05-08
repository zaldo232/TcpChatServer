using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;

Database.SaveChat(new ChatPacket { Type = "system", Sender = "서버", Content = "서버 시작됨" });

TcpListener listener = new TcpListener(IPAddress.Any, 9000);
listener.Start();
Console.WriteLine("서버 실행 중...");

ConcurrentDictionary<TcpClient, NetworkStream> clients = new();

_ = Task.Run(async () =>
{
    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        Console.WriteLine("클라이언트 접속됨");

        var stream = client.GetStream();
        clients.TryAdd(client, stream);
        _ = HandleClientAsync(client, stream);
    }
});

async Task HandleClientAsync(TcpClient client, NetworkStream stream)
{
    try
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            string? json = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(json)) break;

            var packet = JsonSerializer.Deserialize<ChatPacket>(json);
            if (packet == null) continue;

            Console.WriteLine($"[{packet.Sender}] {packet.Type}: {packet.Content}");
            Database.SaveChat(packet);

            if (packet.Type == "file")
            {
                string saveDir = Path.Combine("ReceivedFiles");
                Directory.CreateDirectory(saveDir);
                string path = Path.Combine(saveDir, packet.FileName);
                File.WriteAllBytes(path, Convert.FromBase64String(packet.Content));
                Console.WriteLine($"파일 저장됨: {path}");
            }

            foreach (var kvp in clients)
            {
                var otherStream = kvp.Value;
                if (otherStream != stream)
                {
                    byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                    await otherStream.WriteAsync(data, 0, data.Length);
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"에러: {ex.Message}");
    }
    finally
    {
        Console.WriteLine("클라이언트 연결 종료");
        clients.TryRemove(client, out _);
        stream.Close();
        client.Close();
    }
}


Console.ReadLine();
