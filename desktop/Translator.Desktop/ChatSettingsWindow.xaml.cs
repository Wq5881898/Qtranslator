using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Translator.Core;

namespace Translator.Desktop;

public partial class ChatSettingsWindow : Window
{
    private readonly ObservableCollection<ChatProfile> _profiles;
    private ChatProfile? _editing;
    private bool _changing;
    public ChatSettings Settings { get; private set; }

    public ChatSettingsWindow(ChatSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        _profiles = new(settings.Profiles.Select(p => p with { }));
        ProfilesList.ItemsSource = _profiles;
        ProfilesList.SelectedItem = _profiles.FirstOrDefault(p => p.Id == settings.SelectedId) ?? _profiles.First();
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changing) return;
        try
        {
            ReadEditor();
            _editing = ProfilesList.SelectedItem as ChatProfile;
            if (_editing is null) return;
            ProviderName.Text = _editing.Name; BaseUrl.Text = _editing.BaseUrl;
            ApiFormat.SelectedIndex = (int)_editing.Api; Model.Text = _editing.Model;
            ApiKey.Password = _editing.ApiKey; Authentication.SelectedIndex = (int)_editing.Authentication;
            AuthHeader.Text = _editing.AuthHeader; AuthPrefix.Text = _editing.AuthPrefix;
            ContextTokens.Text = _editing.ContextTokens?.ToString() ?? "";
            MaxTokens.Text = _editing.MaxTokens?.ToString() ?? "";
            SystemPrompt.Text = _editing.SystemPrompt; Stream.IsChecked = _editing.Stream;
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            _changing = true; ProfilesList.SelectedItem = _editing; _changing = false;
        }
    }

    private void ReadEditor()
    {
        if (_editing is null) return;
        int? Parse(string text) => string.IsNullOrWhiteSpace(text) ? null :
            int.TryParse(text, out var n) && n > 0 ? n : throw new InvalidOperationException("Token 上限必须是正整数或留空。");
        var updated = _editing with
        {
            Name = ProviderName.Text.Trim(), BaseUrl = BaseUrl.Text.Trim(), Api = (ChatApiFormat)ApiFormat.SelectedIndex,
            Model = Model.Text.Trim(), ApiKey = ApiKey.Password.Trim(), Authentication = (ChatAuthentication)Authentication.SelectedIndex,
            AuthHeader = AuthHeader.Text.Trim(), AuthPrefix = AuthPrefix.Text, ContextTokens = Parse(ContextTokens.Text),
            MaxTokens = Parse(MaxTokens.Text), SystemPrompt = SystemPrompt.Text, Stream = Stream.IsChecked == true
        };
        updated.Validate(requireKey: false);
        var index = _profiles.IndexOf(_editing);
        _changing = true;
        _profiles[index] = updated;
        _editing = updated;
        // Do not replace a new selection while processing SelectionChanged.
        _changing = false;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadEditor();
            var profile = new ChatProfile { Model = "填写模型ID" };
            _profiles.Add(profile); ProfilesList.SelectedItem = profile;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null || _profiles.Count <= 1) { ErrorText.Text = "至少保留一个服务档案。"; return; }
        _changing = true; _profiles.Remove(_editing); _editing = null; _changing = false;
        ProfilesList.SelectedItem = _profiles.First();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadEditor();
            Settings = new(_profiles.ToList(), _editing!.Id);
            ChatSettingsStore.Save(Settings);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
