using System.Windows;
using System.Windows.Controls;

using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;
    private readonly EventBus? _eventBus;
    private bool _suppressEvents;
    private bool _credentialsChanged;

    public SettingsWindow(ConfigService configService, EventBus? eventBus = null)
    {
        InitializeComponent();
        _configService = configService;
        _eventBus = eventBus;

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        _suppressEvents = true;

        var config = _configService.Config;

        // 账户
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            ApiKeyBox.Password = config.ApiKey;
        BaseUrlBox.Text = config.ApiBaseUrl;

        // 模型
        foreach (ComboBoxItem item in ModelCombo.Items)
        {
            if ((string)item.Tag == config.Model)
            {
                ModelCombo.SelectedItem = item;
                break;
            }
        }
        MaxTokensBox.Text = config.MaxTokens.ToString();

        // Thinking
        ThinkingToggle.IsChecked = config.ThinkingEnabled;
        foreach (ComboBoxItem item in EffortCombo.Items)
        {
            if ((string)item.Tag == config.ReasoningEffort)
            {
                EffortCombo.SelectedItem = item;
                break;
            }
        }

        // 生成参数
        TempSlider.Value = config.Temperature;
        TopPSlider.Value = config.TopP;
        FreqSlider.Value = config.FrequencyPenalty;
        PresSlider.Value = config.PresencePenalty;

        // Beta
        JsonOutputToggle.IsChecked = config.EnableJsonOutput;
        PrefixCompletionToggle.IsChecked = config.EnablePrefixCompletion;
        PrefixContentBox.Text = config.PrefixContent;

        _suppressEvents = false;
        _credentialsChanged = false;
    }

    private void OnSettingChanged(object sender, EventArgs e)
    {
        if (_suppressEvents) return;
    }

    private void TempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        TempLabel.Text = e.NewValue.ToString("F1");
    }

    private void TopPSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        TopPLabel.Text = e.NewValue.ToString("F2");
    }

    private void FreqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        FreqLabel.Text = e.NewValue.ToString("F1");
    }

    private void PresSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents) return;
        PresLabel.Text = e.NewValue.ToString("F1");
    }

    private void ToggleApiKeyVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (ApiKeyBox.PasswordChar == '•')
        {
            ApiKeyBox.PasswordChar = '\0';
            ToggleApiKeyBtn.Content = "🙈";
        }
        else
        {
            ApiKeyBox.PasswordChar = '•';
            ToggleApiKeyBtn.Content = "👁";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var config = _configService.Config;

        // 检测凭据变更
        var newApiKey = ApiKeyBox.Password.Trim();
        var newBaseUrl = BaseUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newBaseUrl))
            newBaseUrl = "https://api.deepseek.com";

        if (newApiKey != config.ApiKey || newBaseUrl != config.ApiBaseUrl)
            _credentialsChanged = true;

        // 账户
        config.ApiKey = newApiKey;
        config.ApiBaseUrl = newBaseUrl;

        // 模型
        if (ModelCombo.SelectedItem is ComboBoxItem modelItem)
            config.Model = (string)modelItem.Tag;

        if (int.TryParse(MaxTokensBox.Text, out var maxTokens) && maxTokens > 0)
            config.MaxTokens = Math.Min(maxTokens, 384000);

        // Thinking
        config.ThinkingEnabled = ThinkingToggle.IsChecked == true;

        if (EffortCombo.SelectedItem is ComboBoxItem effortItem)
            config.ReasoningEffort = (string)effortItem.Tag;

        // 生成参数
        config.Temperature = TempSlider.Value;
        config.TopP = TopPSlider.Value;
        config.FrequencyPenalty = FreqSlider.Value;
        config.PresencePenalty = PresSlider.Value;

        // Beta
        config.EnableJsonOutput = JsonOutputToggle.IsChecked == true;
        config.EnablePrefixCompletion = PrefixCompletionToggle.IsChecked == true;
        config.PrefixContent = PrefixContentBox.Text.Trim();

        _configService.Save(config);
        _eventBus?.Publish(new ConfigChangedEvent
        {
            Model = config.Model,
            CredentialsChanged = _credentialsChanged
        });

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
