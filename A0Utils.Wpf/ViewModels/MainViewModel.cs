using A0Utils.Wpf.Helpers;
using A0Utils.Wpf.Models;
using A0Utils.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CSharpFunctionalExtensions;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;

namespace A0Utils.Wpf.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly FileOperationsService _fileOperationsService;
        private readonly YandexService _yandexService;
        private readonly DialogService _dialogService;
        private readonly SettingsService _settingsService;
        private readonly UpdateService _updateService;

        private static readonly string[] _assemblyInfo = AssemblyHelpers.GetAssemblyInfo();

        public MainViewModel(
            FileOperationsService fileOperationsService,
            YandexService yandexService,
            DialogService dialogService,
            SettingsService settingsService,
            UpdateService updateService)
        {
            _fileOperationsService = fileOperationsService;
            _yandexService = yandexService;
            _dialogService = dialogService;
            _settingsService = settingsService;
            _updateService = updateService;

            var settings = _settingsService.GetSettings();

            FindAllLicenses();
            DownloadPath = settings.DownloadUpdatesPath;

            _yandexService.DownloadUpdatesProgressChanged += (s, progress) => DownloadProgress = progress;
        }


        public static string AssemblyVersion { get { return _assemblyInfo[0]; } }
        public static string AssemblyCopyright { get { return _assemblyInfo[1]; } }
        public static string AssemblyCompany { get { return _assemblyInfo[2]; } }

        public string SelectedLicense { get; set; }

        private string _downloadPath;
        public string DownloadPath
        {
            get => _downloadPath;
            set => SetProperty(ref _downloadPath, value);
        }

        private int _downloadProgress;
        public int DownloadProgress
        {
            get => _downloadProgress;
            set=> SetProperty(ref _downloadProgress, value);
        }

        private string _a0LicenseExp;
        public string A0LicenseExp
        {
            get => _a0LicenseExp;
            set => SetProperty(ref _a0LicenseExp, value);
        }

        private string _pirLicenseExp;
        public string PIRLicenseExp
        {
            get => _pirLicenseExp;
            set => SetProperty(ref _pirLicenseExp, value);
        }

        private string _subscriptionLicenseExp;
        public string SubscriptionLicenseExp
        {
            get => _subscriptionLicenseExp;
            set => SetProperty(ref _subscriptionLicenseExp, value);
        }

        private ObservableCollection<string> _licenses;
        public ObservableCollection<string> Licenses
        {
            get => _licenses;
            set => SetProperty(ref _licenses, value);
        }

        private ObservableCollection<UpdateModel> _updateModels;
        public ObservableCollection<UpdateModel> UpdateModels
        {
            get => _updateModels;
            set => SetProperty(ref _updateModels, value);
        }

        private ObservableCollection<UpdateModel> _updateModelsWithoutLicense;
        public ObservableCollection<UpdateModel> UpdateModelsWithoutLicense
        {
            get => _updateModelsWithoutLicense;
            set =>SetProperty(ref _updateModelsWithoutLicense, value);
        }

        private ICommand _checkForAppUpdateCommand;
        public ICommand CheckForAppUpdateCommand
        {
            get
            {
                return _checkForAppUpdateCommand ??= new AsyncRelayCommand(CheckForAppUpdate);
            }
        }

        private async Task CheckForAppUpdate()
        {
            try
            {
                var currentVersion = AssemblyVersion;
                var appVersion = await _updateService.CheckForUpdates();
                if (new Version(appVersion.LastVersion) > new Version(currentVersion))
                {
                    var confirmResult = MessageDialogHelper.Confirm("Доступно обновление приложения! Скачать новую версию?");
                    if (confirmResult == DialogResult.Yes)
                    {
                        await _updateService.DownloadLastVersion(appVersion.ReleaseUrl, DownloadPath, appVersion.Name);
                        MessageDialogHelper.ShowInfo($"Обновление приложения загружено {DownloadPath}\n{appVersion.Name}");
                    }
                }
                else
                {
                    MessageDialogHelper.ShowInfo("У вас установлена последняя версия приложения.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при проверке обновлений приложения");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }


        private ICommand _getLicenseInfoCommand;
        public ICommand GetLicenseInfoCommand
        {
            get
            {
                return _getLicenseInfoCommand ??= new AsyncRelayCommand(GetLicenseInfo);
            }
        }

        private async Task GetLicenseInfo()
        {
            try
            {
                UpdateModels?.Clear();
                UpdateModelsWithoutLicense?.Clear();

                if (string.IsNullOrEmpty(SelectedLicense))
                {
                    MessageDialogHelper.ShowError("Выберите лицензию из списка");
                    return;
                }

                var downloadLicenseResult = await DownloadAndCopyLicense(SelectedLicense);
                if (downloadLicenseResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(downloadLicenseResult.Error);
                    return;
                }

                var licenseResult = await _yandexService.GetLicensesInfo(SelectedLicense);
                if (licenseResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(licenseResult.Error);
                    return;
                }

                A0LicenseExp = licenseResult.Value.A0LicenseExpAt == default
                    ? "Лицензия А0 отсутствует"
                    : $"Лицензия А0 до: {licenseResult.Value.A0LicenseExpAt:dd.MM.yyyy}";

                PIRLicenseExp = licenseResult.Value.PIRLicenseExpAt == default
                    ? "Лицензия ПИР отсутствует"
                    : $"Лицензия ПИР до: {licenseResult.Value.PIRLicenseExpAt:dd.MM.yyyy}";

                SubscriptionLicenseExp = licenseResult.Value.SubscriptionLicenseExpAt == default
                    ? "Подписка на базы отсутствует"
                    : licenseResult.Value.SubscriptionLicenseExpAt > DateTime.Now
                        ? $"Подписка на базы до: {licenseResult.Value.SubscriptionLicenseExpAt:dd.MM.yyyy}"
                        : $"Подписка на базы закончилась {licenseResult.Value.SubscriptionLicenseExpAt:dd.MM.yyyy}";

                var updatesResult = await _yandexService.GetUpdates();
                if (updatesResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(updatesResult.Error);
                    return;
                }

                var updateCollectionResult = UpdateModelExtensions.ApplyFilter(updatesResult.Value, licenseResult.Value);
                if (updateCollectionResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(updateCollectionResult.Error);
                    return;
                }

                UpdateModels = new ObservableCollection<UpdateModel>(updateCollectionResult.Value.FilteredLicenses);
                UpdateModelsWithoutLicense = new ObservableCollection<UpdateModel>(updateCollectionResult.Value.AllLicenses);

                MessageDialogHelper.ShowInfo("Информация о лицензии получена!");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }

        private ICommand _downloadSelectedCommand;
        public ICommand DownloadSelectedCommand
        {
            get
            {
                return _downloadSelectedCommand ??= new AsyncRelayCommand(DownloadSelected);
            }
        }

        private async Task DownloadSelected()
        {
            if (UpdateModels is null)
            {
                MessageDialogHelper.ShowError("Выберите лицензию из списка");
                return;
            }

            var selectedUpdates = UpdateModels.Where(item => item.IsSelected).ToList();
            if (selectedUpdates.Count == 0)
            {
                MessageDialogHelper.ShowError("Выберите обновления для загрузки");
                return;
            }

            var downloadResult = await _yandexService.DownloadUpdates(selectedUpdates, DownloadPath);
            if (downloadResult.IsFailure)
            {
                MessageDialogHelper.ShowError(downloadResult.Error);
                return;
            }

            MessageDialogHelper.ShowInfo("Обновления загружены!");
            OpenDownloadFolder();
        }


        private ICommand _saveToCommand;
        public ICommand SaveToCommand
        {
            get
            {
                return _saveToCommand ??= new RelayCommand(SaveTo);
            }
        }

        private void SaveTo()
        {
            FolderBrowserDialog folderDialog = new FolderBrowserDialog();

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                var path = Path.Combine(folderDialog.SelectedPath, "A0Updates");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                DownloadPath = path;
                _settingsService.UpdateDownloadPath(DownloadPath);
            }

            MessageDialogHelper.ShowInfo("Путь сохранения обновлений изменен!");
        }

        private void OpenDownloadFolder()
        {            
            try
            {
                if (!Directory.Exists(DownloadPath))
                {
                    Directory.CreateDirectory(DownloadPath);
                }
                System.Diagnostics.Process.Start("explorer.exe", DownloadPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при открытии папки загрузок");
                MessageDialogHelper.ShowError($"Ошибка при открытии папки загрузок: {ex.Message}");
            }
        }

        private ICommand _openSettingsCommand;
        public ICommand OpenSettingsCommand
        {
            get
            {
                return _openSettingsCommand ??= new RelayCommand(_dialogService.ShowSettingsDialog);
            }
        }

        private ICommand _openLicenseCommand;
        public ICommand OpenLicenseCommand
        {
            get
            {
                return _openLicenseCommand ??= new RelayCommand(_dialogService.ShowLicenseDialog);
            }
        }

        private ICommand _refreshLicensesCommand;
        public ICommand RefreshLicensesCommand
        {
            get
            {
                return _refreshLicensesCommand ??= new RelayCommand(FindAllLicenses);
            }
        }

        private void FindAllLicenses()
        {
            var settings = _settingsService.GetSettings();
            var a0InstallationPath = settings.A0InstallationPath;
            try
            {
                if (_fileOperationsService.IsFolderExist(a0InstallationPath))
                {

                    var foundLicense = _fileOperationsService.FindAllLicFiles(a0InstallationPath);
                    if (!foundLicense.Any())
                    {
                        MessageDialogHelper.ShowError("Лицензионные файлы не найдены");
                    }
                    else
                    {
                        Licenses = new ObservableCollection<string>(foundLicense.Select(x => x.FileName).Distinct());
                    }
                }
                else
                {
                    Log.Error("Программа A0 не установлена или отсутствует доступ к папке {Path}", a0InstallationPath);
                    MessageDialogHelper.ShowError($"Программа A0 не установлена или отсутствует доступ к папке {a0InstallationPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }

        private async Task<Result> DownloadAndCopyLicense(string licenseName)
        {
            var licenseResult = await _yandexService.DownloadLicense(licenseName);
            if (licenseResult.IsFailure)
            {
                return Result.Failure(licenseResult.Error);
            }

            var settings = _settingsService.GetSettings();
            var copyResult = licenseResult.Value.CopyToAllFolders(_fileOperationsService, settings.A0InstallationPath);
            if (copyResult.IsFailure)
            {
                return Result.Failure(copyResult.Error);
            }

            return Result.Success();
        }
    }
}
