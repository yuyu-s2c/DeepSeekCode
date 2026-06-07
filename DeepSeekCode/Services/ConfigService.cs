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
                    return config;
            }
        }
        catch (JsonException ex)
        {
            // 配置文件 JSON 格式损坏，使用默认配置
            System.Diagnostics.Debug.WriteLine($"[ConfigService] JSON 解析失败: {ex.Message}");
        }
        catch (IOException ex)
        {
            // 文件读取失败（权限/锁定等），使用默认配置
            System.Diagnostics.Debug.WriteLine($"[ConfigService] 文件读取失败: {ex.Message}");
        }

        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFile, json);
        _config = config;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(Config.ApiKey);
}
