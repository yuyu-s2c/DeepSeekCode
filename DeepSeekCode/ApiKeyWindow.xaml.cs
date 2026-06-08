using System.Windows;

using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode;

public partial class ApiKeyWindow : Window
{
    private readonly ConfigService _configService;

    public ApiKeyWindow(ConfigService configService)
    {
        InitializeComponent();
        _configService = configService;

        if (!string.IsNullOrWhiteSpace(configService.Config.ApiBaseUrl))
            BaseUrlBox.Text = configService.Config.ApiBaseUrl;

        ApiKeyBox.Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("请输入 API Key。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = new AppConfig
        {
            ApiKey = apiKey,
            ApiBaseUrl = BaseUrlBox.Text.Trim(),
            Model = "deepseek-v4-pro",
            MaxTokens = 8192,
            Temperature = 0.7
        };

        _configService.Save(config);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
