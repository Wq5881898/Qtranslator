using System.Windows;

namespace Qtranslator.Desktop;

public partial class ShortcutSettingsWindow : Window
{
    public ShortcutSettings SelectedSettings { get; private set; }

    public ShortcutSettingsWindow(ShortcutSettings current)
    {
        InitializeComponent();
        ModifiersBox.ItemsSource = Enum.GetValues<ShortcutModifiers>();
        KeyBox.ItemsSource = Enumerable.Range('A', 26).Select(value => ((char)value).ToString());
        ModifiersBox.SelectedItem = current.Modifiers;
        KeyBox.SelectedItem = current.Key.ToUpperInvariant();
        SelectedSettings = current;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ModifiersBox.SelectedItem is not ShortcutModifiers modifiers ||
            KeyBox.SelectedItem is not string key)
        {
            return;
        }

        var settings = new ShortcutSettings(modifiers, key);
        if (!ShortcutSettingsStore.IsAllowed(settings))
        {
            HintText.Text = "Ctrl+Shift+X 留给旧 Translator；Ctrl+Shift+D 用于 AI 截图。请选择其他组合。";
            return;
        }
        SelectedSettings = settings;
        DialogResult = true;
    }
}
