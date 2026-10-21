using System.Text;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Qtranslator.Core;

namespace Qtranslator.Desktop;

public partial class FavoritesWindow : Window
{
    private List<FavoriteEntry> _favorites = [];

    public FavoritesWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _favorites = (await SharedFavoriteStore.LoadAsync()).ToList();
        FavoritesGrid.ItemsSource = null;
        FavoritesGrid.ItemsSource = _favorites;
        CountText.Text = $"{_favorites.Count} saved locally";
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = FavoritesGrid.SelectedItems
            .OfType<FavoriteEntry>()
            .Select(item => item.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            StatusText.Text = "Select one or more favorites to remove.";
            return;
        }

        try
        {
            await SharedFavoriteStore.PatchAsync([], selectedIds);
            await ReloadAsync();
            StatusText.Text = selectedIds.Length == 1
                ? "1 favorite removed."
                : $"{selectedIds.Length} favorites removed.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Remove failed: {exception.Message}";
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV files (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = FavoritesCsv.Parse(await File.ReadAllTextAsync(dialog.FileName));
            await SharedFavoriteStore.PatchAsync(imported, []);
            await ReloadAsync();
            StatusText.Text = "Favorites imported.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Import failed: {exception.Message}";
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"translator-favorites-{DateTime.Now:yyyy-MM-dd}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(
                dialog.FileName,
                FavoritesCsv.Serialize(_favorites),
                new UTF8Encoding(true));
            StatusText.Text = "Favorites exported.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Export failed: {exception.Message}";
        }
    }
}

