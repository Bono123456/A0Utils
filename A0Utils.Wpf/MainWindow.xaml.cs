using Serilog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace A0Utils.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // Сворачивание раздела "Ресурсы, которые можно лицензировать дополнительно":
        // в свёрнутом виде строка таблицы не занимает места, и окно помещается на небольших экранах
        private void ExtraResourcesToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ExtraResourcesPanel == null)
            {
                return;
            }

            var isExpanded = ExtraResourcesToggle.IsChecked == true;
            ExtraResourcesPanel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            ExtraResourcesRow.Height = isExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        // Открывает почтовую программу по клику на адрес
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось открыть почтовую программу");
                Helpers.MessageDialogHelper.ShowInfo("Не удалось открыть почтовую программу. Адрес: nik@rccs.sampo.ru");
            }

            e.Handled = true;
        }
    }
}
