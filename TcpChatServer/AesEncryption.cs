using System.Security.Cryptography;
using System.Text;

public static class AesEncryption
{
    private static readonly string Key = "1234567890ABCDEF";    // 16 bytes
    private static readonly string IV = "ABCDEF1234567890";     // 16 bytes

    public static string Encrypt(string plainText)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(Key);
            aes.IV = Encoding.UTF8.GetBytes(IV);

            using var ms = new MemoryStream();
            using var encryptor = aes.CreateEncryptor();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            var result = Convert.ToBase64String(ms.ToArray());
            System.Diagnostics.Debug.WriteLine($"[암호화 결과] {result}");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[암호화 실패] {ex.Message}");
            return string.Empty;
        }
    }

    public static string Decrypt(string encryptedText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(Key);
        aes.IV = Encoding.UTF8.GetBytes(IV);

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(Convert.FromBase64String(encryptedText));
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }
}