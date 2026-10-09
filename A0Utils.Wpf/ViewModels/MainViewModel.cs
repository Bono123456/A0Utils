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

        private string _selectedLicense;
        public string SelectedLicense
        {
            get => _selectedLicense;
            set
            {
                if (SetProperty(ref _selectedLicense, value))
                {
                    ClearLoadedLicense();
                }
            }
        }

        // Лицензия, для которой сейчас показаны списки обновлений и ресурсов
        private string _loadedLicense;
        private LicenseInfoModel _loadedLicenseInfo;

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

        private bool _isA0Expired;
        public bool IsA0Expired
        {
            get => _isA0Expired;
            set => SetProperty(ref _isA0Expired, value);
        }

        private bool _isPIRExpired;
        public bool IsPIRExpired
        {
            get => _isPIRExpired;
            set => SetProperty(ref _isPIRExpired, value);
        }

        private bool _isSubscriptionExpired;
        public bool IsSubscriptionExpired
        {
            get => _isSubscriptionExpired;
            set => SetProperty(ref _isSubscriptionExpired, value);
        }

        private ObservableCollection<UpdateModel> _updateModels;
        public ObservableCollection<UpdateModel> UpdateModels
        {
            get => _updateModels;
            set
            {
                if (_updateModels != null)
                {
                    foreach (var item in _updateModels)
                    {
                        item.PropertyChanged -= OnUpdateModelPropertyChanged;
                    }
                }

                SetProperty(ref _updateModels, value);

                if (_updateModels != null)
                {
                    foreach (var item in _updateModels)
                    {
                        item.PropertyChanged += OnUpdateModelPropertyChanged;
                    }
                }

                OnPropertyChanged(nameof(AreAllSelected));
            }
        }

        // Галочка в заголовке таблицы: отмечает или снимает все обновления.
        // true — отмечены все, false — ни одного, null — часть.
        public bool? AreAllSelected
        {
            get
            {
                if (UpdateModels == null || UpdateModels.Count == 0)
                {
                    return false;
                }

                var selectedCount = UpdateModels.Count(x => x.IsSelected);
                if (selectedCount == 0)
                {
                    return false;
                }

                return selectedCount == UpdateModels.Count ? true : (bool?)null;
            }
            set
            {
                if (UpdateModels == null)
                {
                    return;
                }

                var isSelected = value == true;
                foreach (var item in UpdateModels)
                {
                    item.IsSelected = isSelected;
                }

                OnPropertyChanged(nameof(AreAllSelected));
            }
        }

        private void OnUpdateModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UpdateModel.IsSelected))
            {
                OnPropertyChanged(nameof(AreAllSelected));
            }
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
            var licenseName = SelectedLicense;
            ClearLoadedLicense();

            try
            {
                if (string.IsNullOrEmpty(licenseName))
                {
                    MessageDialogHelper.ShowError("Выберите лицензию из списка");
                    return;
                }

                var downloadLicenseResult = await DownloadAndCopyLicense(licenseName);
                if (SelectedLicense != licenseName)
                {
                    return;
                }

                if (downloadLicenseResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(downloadLicenseResult.Error);
                    return;
                }

                var licenseResult = await _yandexService.GetLicensesInfo(licenseName);
                if (SelectedLicense != licenseName)
                {
                    return;
                }

                if (licenseResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(licenseResult.Error);
                    return;
                }

                var updatesResult = await _yandexService.GetUpdates();
                if (SelectedLicense != licenseName)
                {
                    return;
                }

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

                // Prepare both lists before publishing the successfully loaded license.
                var updates = new ObservableCollection<UpdateModel>(updateCollectionResult.Value.FilteredLicenses);
                var extraResources = new ObservableCollection<UpdateModel>(updateCollectionResult.Value.AllLicenses);
                var today = DateTime.Today;
                IsA0Expired = licenseResult.Value.A0LicenseExpAt != default && licenseResult.Value.A0LicenseExpAt < today;
                IsPIRExpired = licenseResult.Value.PIRLicenseExpAt != default && licenseResult.Value.PIRLicenseExpAt < today;
                IsSubscriptionExpired = licenseResult.Value.SubscriptionLicenseExpAt != default && licenseResult.Value.SubscriptionLicenseExpAt < today;

                A0LicenseExp = licenseResult.Value.A0LicenseExpAt == default
                    ? "Лицензия А0 отсутствует"
                    : IsA0Expired
                        ? $"Лицензия А0 закончилась {licenseResult.Value.A0LicenseExpAt:dd.MM.yyyy}"
                        : $"Лицензия А0 до: {licenseResult.Value.A0LicenseExpAt:dd.MM.yyyy}";

                PIRLicenseExp = licenseResult.Value.PIRLicenseExpAt == default
                    ? "Лицензия ПИР отсутствует"
                    : IsPIRExpired
                        ? $"Лицензия ПИР закончилась {licenseResult.Value.PIRLicenseExpAt:dd.MM.yyyy}"
                        : $"Лицензия ПИР до: {licenseResult.Value.PIRLicenseExpAt:dd.MM.yyyy}";

                SubscriptionLicenseExp = licenseResult.Value.SubscriptionLicenseExpAt == default
                    ? "Подписка на базы отсутствует"
                    : !IsSubscriptionExpired
                        ? $"Подписка на базы до: {licenseResult.Value.SubscriptionLicenseExpAt:dd.MM.yyyy}"
                        : $"Подписка на базы закончилась {licenseResult.Value.SubscriptionLicenseExpAt:dd.MM.yyyy}";

                UpdateModels = updates;
                UpdateModelsWithoutLicense = extraResources;
                _loadedLicense = licenseName;
                _loadedLicenseInfo = licenseResult.Value;
                _openRequestInvoiceCommand?.NotifyCanExecuteChanged();

                MessageDialogHelper.ShowInfo("Информация о лицензии получена!");
            }
            catch (Exception ex)
            {
                ClearLoadedLicense();
                Log.Error(ex, "Ошибка");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }

        private void ClearLoadedLicense()
        {
            _loadedLicense = null;
            _loadedLicenseInfo = null;
            _openRequestInvoiceCommand?.NotifyCanExecuteChanged();
            UpdateModels = new ObservableCollection<UpdateModel>();
            UpdateModelsWithoutLicense = new ObservableCollection<UpdateModel>();
            A0LicenseExp = null;
            PIRLicenseExp = null;
            SubscriptionLicenseExp = null;
            IsA0Expired = false;
            IsPIRExpired = false;
            IsSubscriptionExpired = false;
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

        private RelayCommand _openRequestInvoiceCommand;
        public ICommand OpenRequestInvoiceCommand
        {
            get
            {
                return _openRequestInvoiceCommand ??= new RelayCommand(RequestInvoice, CanRequestInvoice);
            }
        }

        private bool CanRequestInvoice() =>
            !string.IsNullOrEmpty(_loadedLicense) && _loadedLicense == SelectedLicense
            && _loadedLicenseInfo != null && UpdateModelsWithoutLicense != null;

        // Передаёт в диалог снимок выбранных ресурсов и успешно загруженной лицензии.
        private void RequestInvoice()
        {
            if (!CanRequestInvoice())
            {
                MessageDialogHelper.ShowError("Сначала выберите лицензию и нажмите «Получить обновления»");
                return;
            }

            var resources = UpdateModelsWithoutLicense.Where(x => x.IsSelected).Select(DescribeResource).ToList();
            var request = new InvoiceRequestViewModel(_loadedLicense, _loadedLicenseInfo,
                resources, SupportEmail, AssemblyVersion, DateTime.Today);
            if (resources.Count == 0 && !request.HasRenewals)
            {
                MessageDialogHelper.ShowError("Отметьте ресурсы, на которые нужно выставить счёт");
                return;
            }

            _dialogService.ShowInvoiceRequestDialog(request);
        }

        private const string SupportEmail = "nik@rccs.sampo.ru";

        private static string DescribeResource(UpdateModel model)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (model.Category == "Справочники цен")
            {
                parts.Add($"Справочник цен {model.Index}, {model.Name}");
                if (!string.IsNullOrWhiteSpace(model.Date))
                {
                    parts.Add($"от {model.Date}");
                }
            }
            else
            {
                parts.Add($"{model.Category}: {model.Name}");
                if (!string.IsNullOrWhiteSpace(model.Index))
                {
                    parts.Add($"({model.Index})");
                }
            }

            return string.Join(" ", parts);
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
