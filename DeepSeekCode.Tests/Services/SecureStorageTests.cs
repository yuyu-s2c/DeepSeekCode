using DeepSeekCode.Services;

namespace DeepSeekCode.Tests.Services;

public class SecureStorageTests
{
    [Fact]
    public void Encrypt_ProducesVersionedBase64()
    {
        var plaintext = "sk-test-key-12345";
        var encrypted = SecureStorage.Encrypt(plaintext);

        Assert.NotNull(encrypted);
        Assert.StartsWith("DPAPI:v1:", encrypted);
        Assert.True(Convert.TryFromBase64String(encrypted["DPAPI:v1:".Length..], new byte[1024], out _));
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        var original = "test-api-key-abcdefghijklmnop";
        var encrypted = SecureStorage.Encrypt(original);
        var decrypted = SecureStorage.Decrypt(encrypted);

        Assert.NotNull(decrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Decrypt_ReturnsNull_ForNonEncryptedString()
    {
        Assert.Null(SecureStorage.Decrypt("plaintext-key"));
        Assert.Null(SecureStorage.Decrypt(""));
        Assert.Null(SecureStorage.Decrypt("not-a-prefixed-value"));
    }

    [Fact]
    public void Decrypt_ReturnsNull_ForInvalidBase64()
    {
        Assert.Null(SecureStorage.Decrypt("DPAPI:v1:!!!invalid-base64!!!"));
    }

    [Fact]
    public void IsEncrypted_DetectsCorrectly()
    {
        Assert.True(SecureStorage.IsEncrypted("DPAPI:v1:abcd"));
        Assert.False(SecureStorage.IsEncrypted("plain-text"));
        Assert.False(SecureStorage.IsEncrypted(""));
    }

    [Fact]
    public void Encrypt_UnicodeText_Works()
    {
        var original = "中文密钥-日本語キー-한국어키";
        var encrypted = SecureStorage.Encrypt(original);
        var decrypted = SecureStorage.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }
}
