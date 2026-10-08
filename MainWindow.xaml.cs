using Serilog;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
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

        // Высота раздела заявки, которую пользователь выставил границей перед сворачиванием
        private GridLength _extraResourcesHeight = new GridLength(1, GridUnitType.Star);

        // Сворачивание раздела "Оформить заявку на лицензию":
        // в свёрнутом виде он занимает одну строку, и окно помещается на небольших экранах.
        // В раскрытом виде между списками появляется граница, которую можно тянуть мышью.
        private void ExtraResourcesToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ExtraResourcesPanel == null)
            {
                return;
            }

            var isExpanded = ExtraResourcesToggle.IsChecked == true;
            if (!isExpanded && !ExtraResourcesRow.Height.IsAuto)
            {
                _extraResourcesHeight = ExtraResourcesRow.Height;
            }

            ExtraResourcesPanel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            SectionSplitter.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            ExtraResourcesRow.Height = isExpanded ? _extraResourcesHeight : GridLength.Auto;
            ExtraResourcesRow.MinHeight = isExpanded ? 150 : 0;
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

    // Решает, раскрыта ли группа в списке по умолчанию. Смотрит на первый элемент группы
    // (через вложенные группы): параметр 1 — группа первого уровня, 2 — второго.
    public sealed class GroupExpandConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            object item = value;
            while (item is CollectionViewGroup group)
            {
                item = group.Items.FirstOrDefault();
            }

            if (item is Models.UpdateModel model)
            {
                return parameter as string == "2" ? model.ExpandGroup2 : model.ExpandGroup1;
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
