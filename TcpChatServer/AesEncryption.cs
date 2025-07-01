using System.Security.Cryptography;
using System.Text;

// AES 대칭키 암호화/복호화 유틸리티 클래스
public static class AesEncryption
{
    // 암호화 키(16바이트, 예시용)  
    private static readonly string Key = "1234567890ABCDEF";    // 16 bytes
    // 초기화 벡터(IV, 16바이트, 예시용)  
    private static readonly string IV = "ABCDEF1234567890";     // 16 bytes

    // 문자열을 AES로 암호화하여 Base64 문자열로 반환
    public static string Encrypt(string plainText)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(Key);    // 키 세팅
            aes.IV = Encoding.UTF8.GetBytes(IV);      // IV 세팅

            using var ms = new MemoryStream();
            using var encryptor = aes.CreateEncryptor();
            // CryptoStream을 이용해 암호화 진행
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);  // 평문 작성
            }

            var result = Convert.ToBase64String(ms.ToArray());  // 암호화된 데이터 Base64 인코딩
            System.Diagnostics.Debug.WriteLine($"[암호화 결과] {result}");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[암호화 실패] {ex.Message}");
            return string.Empty;
        }
    }

    // AES로 암호화된 Base64 문자열을 복호화하여 원본 문자열 반환
    public static string Decrypt(string encryptedText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(Key);   // 키 세팅
        aes.IV = Encoding.UTF8.GetBytes(IV);     // IV 세팅

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(Convert.FromBase64String(encryptedText));
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd(); // 복호화 결과 반환
    }

    // 바이트 배열을 AES로 암호화하여 암호문 바이트 배열 반환
    public static byte[] EncryptBytes(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(Key);
        aes.IV = Encoding.UTF8.GetBytes(IV);
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length); // 암호화 블록 반환
    }

    // 암호화된 바이트 배열을 복호화하여 평문 바이트 배열 반환
    public static byte[] DecryptBytes(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(Key);
        aes.IV = Encoding.UTF8.GetBytes(IV);
        using var decryptor = aes.CreateDecryptor();    
        return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);   // 복호화 블록 반환
    }
}