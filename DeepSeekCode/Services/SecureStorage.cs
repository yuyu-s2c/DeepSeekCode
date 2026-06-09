using System.Security.Cryptography;

namespace DeepSeekCode.Services;

/// <summary>
/// 基于 Windows DPAPI 的敏感数据加密封装
/// 使用当前用户凭据加密，解密仅限同一用户在同一机器上
/// </summary>
public static class SecureStorage
{
    private const string VersionPrefix = "DPAPI:v1:";
    private static readonly byte[] EntropyBytes = "DeepSeekCode.SecureStorage"u8.ToArray();

    /// <summary>加密明文，返回带版本前缀的 Base64 密文</summary>
    public static string Encrypt(string plaintext)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = ProtectedData.Protect(plainBytes, EntropyBytes, DataProtectionScope.CurrentUser);
        return VersionPrefix + Convert.ToBase64String(cipherBytes);
    }

    /// <summary>解密密文，返回明文。解密失败返回 null</summary>
    public static string? Decrypt(string ciphertext)
    {
        if (!ciphertext.StartsWith(VersionPrefix))
            return null;

        try
        {
            var base64 = ciphertext[VersionPrefix.Length..];
            var cipherBytes = Convert.FromBase64String(base64);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, EntropyBytes, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>判断字符串是否已被 DPAPI 加密（包含版本前缀）</summary>
    public static bool IsEncrypted(string value) => value.StartsWith(VersionPrefix);
}
