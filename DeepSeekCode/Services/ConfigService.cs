using System.IO;
using System.Text.Json;
using DeepSeekCode.Models;

namespace DeepSeekCode.Services;

public class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepseek-code");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private AppConfig? _config;

    public AppConfig Config
    {
        get
        {
            _config ??= Load();
            return _config;
        }
    }

    private AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null && !string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    if (SecureStorage.IsEncrypted(config.ApiKey))
                    {
                        // 已 DPAPI 加密，解密到内存
                        var decrypted = SecureStorage.Decrypt(config.ApiKey);
                        if (decrypted != null)
                        {
                            config.ApiKey = decrypted;
                            return config;
                        }
                        // 解密失败（用户变更或其他机器迁移），清除 Key 提示重新输入
                        config.ApiKey = "";
                        SaveToDisk(config);
                    }
                    else
                    {
                        // 明文旧格式，自动加密迁移
                        var plaintext = config.ApiKey;
                        config.ApiKey = SecureStorage.Encrypt(plaintext);
                        SaveToDisk(config);
                        config.ApiKey = plaintext;
                    }
                    return config;
                }
                return config ?? new AppConfig();
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigService] JSON 解析失败: {ex.Message}");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigService] 文件读取失败: {ex.Message}");
        }

        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);

        // API Key 在磁盘上永远以 DPAPI 密文存储，创建副本避免修改传入对象
        var diskConfig = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            diskConfig.ApiKey = SecureStorage.Encrypt(config.ApiKey);

        SaveToDisk(diskConfig);
        _config = config;
    }

    private static void SaveToDisk(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFile, json);
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(Config.ApiKey);
}
