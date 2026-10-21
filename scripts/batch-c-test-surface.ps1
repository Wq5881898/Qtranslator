Add-Type -AssemblyName PresentationFramework

$window = New-Object Windows.Window
$window.Title = "Translator Batch C Test Surface"
$window.Left = 100
$window.Top = 100
$window.Width = 900
$window.Height = 320
$window.WindowStartupLocation = "Manual"
$window.Topmost = $true
$window.Background = [Windows.Media.Brushes]::White

$text = New-Object Windows.Controls.TextBlock
$text.Margin = "36"
$text.FontFamily = "Arial"
$text.FontSize = 30
$text.TextWrapping = "Wrap"
$text.Foreground = [Windows.Media.Brushes]::Black
$text.Text = "Learning a language becomes easier when new words are reviewed in meaningful contexts."

$window.Content = $text
$window.ShowDialog() | Out-Null
