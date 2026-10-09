using A0Utils.Wpf.ViewModels;
using A0Utils.Wpf.Views;

namespace A0Utils.Wpf.Services
{
    public sealed class DialogService
    {
        private readonly SettingsViewModel _settingsViewModel;
        private readonly LicenseViewModel _licenseViewModel;

        public DialogService(SettingsViewModel settingsViewModel, 
            LicenseViewModel licenseViewModel)
        {
            _settingsViewModel = settingsViewModel;
            _licenseViewModel = licenseViewModel;
        }

        public void ShowSettingsDialog()
        {
            SettingsView dialog = new SettingsView
            {
                Title = "Утилиты для А0 :: Настройки",
                DataContext = _settingsViewModel,
                Owner = System.Windows.Application.Current?.MainWindow
            };

            void OnRequestClose() => dialog.Close();

            _settingsViewModel.RequestClose += OnRequestClose;
            try
            {
                dialog.ShowDialog();
            }
            finally
            {
                _settingsViewModel.RequestClose -= OnRequestClose;
            }
        }

        public void ShowLicenseDialog()
        {
            LicenseView dialog = new LicenseView
            {
                Title = "Утилиты для А0 :: Лицензии",
                DataContext = _licenseViewModel,
                Owner = System.Windows.Application.Current?.MainWindow
            };

            void OnRequestClose() => dialog.Close();

            _licenseViewModel.RequestClose += OnRequestClose;
            try
            {
                dialog.ShowDialog();
            }
            finally
            {
                _licenseViewModel.RequestClose -= OnRequestClose;
            }
        }

        public void ShowInvoiceRequestDialog(InvoiceRequestViewModel viewModel)
        {
            InvoiceRequestView dialog = new InvoiceRequestView
            {
                Title = "Утилиты для А0 :: Запрос счёта",
                DataContext = viewModel,
                Owner = System.Windows.Application.Current?.MainWindow
            };

            dialog.ShowDialog();
        }
    }
}
