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
using System.Windows;
using System.Windows.Input;

namespace A0Utils.Wpf.ViewModels
{
    public sealed class LicenseViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly FileOperationsService _fileOperationsService;
        private readonly YandexService _yandexService;

        private readonly string _a0InstallationPath;

        public LicenseViewModel(
            SettingsService settingsService,
            FileOperationsService fileOperationsService,
            YandexService yandexService)
        {
            _settingsService = settingsService;
            _fileOperationsService = fileOperationsService;
            _yandexService = yandexService;

            var settings = _settingsService.GetSettings();
            _a0InstallationPath = settings.A0InstallationPath;
            GridVisibility = settings.IsExtraSettingsEnabled ? Visibility.Visible : Visibility.Collapsed;
            FindAllLicenses();

            _yandexService.DownloadLicenseProgressChanged += (s, progress) => DownloadProgress = progress;
        }

        public event Action RequestClose;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private int _downloadProgress;
        public int DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        private Visibility _gridVisibility = Visibility.Collapsed;
        public Visibility GridVisibility
        {
            get => _gridVisibility;
            set => SetProperty(ref _gridVisibility, value);
        }

        private string _licenseName;
        public string LicenseName
        {
            get => _licenseName;
            set => SetProperty(ref _licenseName, value);
        }

        private ObservableCollection<string> _licenses;
        public ObservableCollection<string> Licenses
        {
            get => _licenses;
            set => SetProperty(ref _licenses, value);
        }

        private void FindAllLicenses()
        {
            try
            {
                if (_fileOperationsService.IsFolderExist(_a0InstallationPath))
                {

                    var foundLicense = _fileOperationsService.FindAllLicFiles(_a0InstallationPath);
                    if (!foundLicense.Any())
                    {
                        Licenses = new ObservableCollection<string>();
                        MessageDialogHelper.ShowError("Лицензионные файлы не найдены. Введите номер лицензии, который указан на ключе или в программе А0 (в меню Справка -> О программе)");
                    }
                    else
                    {
                        Licenses = new ObservableCollection<string>(foundLicense.Select(x => x.FileName).Distinct());
                    }
                }
                else
                {
                    Log.Error("Программа A0 не установлена или отсутствует доступ к папке {Path}", _a0InstallationPath);
                    MessageDialogHelper.ShowError($"Программа A0 не установлена или отсутствует доступ к папке {_a0InstallationPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }

        private ICommand _updateLicensesCommand;
        public ICommand UpdateLicensesCommand
        {
            get
            {
                return _updateLicensesCommand ??= new AsyncRelayCommand(UpdateLicenses);
            }
        }

        private async Task UpdateLicenses()
        {
            try
            {
                if (Licenses.Count == 0)
                {
                    MessageDialogHelper.ShowError($"Программа A0 не установлена или отсутствует доступ к папке {_a0InstallationPath}");
                    return;
                }

                foreach (var license in Licenses)
                {
                    await DownloadAndCopyLicense(license);
                }

                MessageDialogHelper.ShowInfo("Лицензии обновлены!");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }

        private ICommand _closeDialogCommand;
        public ICommand CloseDialogCommand
        {
            get
            {
                return _closeDialogCommand ??= new RelayCommand(CloseDialog);
            }
        }

        private void CloseDialog()
        {
            RequestClose?.Invoke();
        }

        private ICommand _addLicenseCommand;
        public ICommand AddLicenseCommand
        {
            get
            {
                return _addLicenseCommand ??= new AsyncRelayCommand(AddLicense);
            }
        }

        private async Task AddLicense()
        {
            try
            {
                //if (Licenses is null || Licenses.Count == 0)
                //{
                //    MessageDialogHelper.ShowError($"Программа A0 не установлена или отсутствует доступ к папке {_a0InstallationPath}");
                //    return;
                //}

                if (string.IsNullOrEmpty(LicenseName))
                {
                    MessageDialogHelper.ShowError("Введите номер лицензии, который указан на ключе или в программе А0 (в меню Справка -> О программе)");
                    return;
                }

                if (LicenseName.Length < 8)
                {
                    LicenseName = LicenseName.PadLeft(8, '0');
                }

                var fileNameResult = await DownloadAndCopyLicense(LicenseName);
                if (fileNameResult.IsFailure)
                {
                    MessageDialogHelper.ShowError(fileNameResult.Error);
                    return;
                }

                Licenses.Add(fileNameResult.Value);
                MessageDialogHelper.ShowInfo($"Лицензия {fileNameResult.Value} добавлена!");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка");
                MessageDialogHelper.ShowError($"Ошибка: {ex.Message}");
            }
        }

        private async Task<Result<string>> DownloadAndCopyLicense(string licenseName)
        {
            IsBusy = true;
            try
            {
                var licenseResult = await _yandexService.DownloadLicense(licenseName);
                if (licenseResult.IsFailure)
                {
                    return Result.Failure<string>(licenseResult.Error);
                }

                var copyResult = licenseResult.Value.CopyToAllFolders(_fileOperationsService, _a0InstallationPath);
                if (copyResult.IsFailure)
                {
                    return Result.Failure<string>(copyResult.Error);
                }

                return Path.GetFileName(licenseResult.Value.LicensePath);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
