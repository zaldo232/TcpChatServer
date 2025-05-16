public class ChatPacket
{
    public int Id { get; set; }
    public string Type { get; set; }
    public string Sender { get; set; }
    public string Receiver { get; set; }
    public string Content { get; set; }
    public string FileName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsRead { get; set; }
    public bool IsDeleted { get; set; }

    public override bool Equals(object obj)
    {
        if (obj is not ChatPacket other) return false;
        return Sender == other.Sender &&
               Receiver == other.Receiver &&
               Timestamp == other.Timestamp &&
               FileName == other.FileName &&
               Content == other.Content &&
               IsDeleted == other.IsDeleted;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Sender, Receiver, Timestamp, FileName, Content, IsDeleted);
    }
}
