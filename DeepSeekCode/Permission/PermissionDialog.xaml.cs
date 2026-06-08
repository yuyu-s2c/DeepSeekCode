using System.Windows;

namespace DeepSeekCode.Services;

public partial class PermissionDialog : Window
{
    public PermissionDecision Decision { get; private set; } = PermissionDecision.Deny;

    public PermissionDialog(string toolName, string command)
    {
        InitializeComponent();

        var displayCmd = command ?? toolName;
        if (displayCmd.Length > 120)
            displayCmd = displayCmd[..120] + "...";

        var icon = toolName switch
        {
            "shell" => ">_",
            "edit_file" => "✎",
            "write_file" => "✎",
            "git_commit" => "◎",
            _ => "⚙"
        };

        PermissionMessage.Text = $"{icon} {toolName}\n\n{displayCmd}";
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = PermissionDecision.Deny;
        DialogResult = false;
        Close();
    }

    private void AllowButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = PermissionDecision.AllowOnce;
        DialogResult = true;
        Close();
    }

    private void AllowAllButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = PermissionDecision.AllowAll;
        DialogResult = true;
        Close();
    }
}
