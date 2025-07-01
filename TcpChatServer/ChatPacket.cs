// 채팅 메시지 패킷 구조 정의 클래스
public class ChatPacket
{
    public int Id { get; set; }                  // 패킷 고유 ID (DB 저장용)
    public string Type { get; set; }             // 패킷 타입(메시지, 파일, 시스템 등)
    public string Sender { get; set; }           // 보낸 사람 닉네임/ID
    public string Receiver { get; set; }         // 받는 사람 닉네임/ID (없으면 전체발송)
    public string Content { get; set; }          // 메시지 내용
    public string FileName { get; set; }         // 첨부 파일명(파일 메시지일 때)
    public DateTime Timestamp { get; set; } = DateTime.Now;   // 전송 시각
    public bool IsRead { get; set; }             // 읽음 여부
    public bool IsDeleted { get; set; }          // 삭제 처리 여부


    // 패킷 동등성 비교 오버라이드
    public override bool Equals(object obj)
    {
        if (obj is not ChatPacket other) return false; // 타입 체크
        return Sender == other.Sender &&
               Receiver == other.Receiver &&
               Timestamp == other.Timestamp &&
               FileName == other.FileName &&
               Content == other.Content &&
               IsDeleted == other.IsDeleted;    // 주요 필드 비교
    }

    // 해시코드 오버라이드 (동등성 비교에 사용)
    public override int GetHashCode()
    {
        return HashCode.Combine(Sender, Receiver, Timestamp, FileName, Content, IsDeleted);
    }
}
    