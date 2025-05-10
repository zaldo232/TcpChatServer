public class ChatPacket
{
    public string Type { get; set; }
    public string Sender { get; set; }
    public string Receiver { get; set; }
    public string Content { get; set; }
    public string FileName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}