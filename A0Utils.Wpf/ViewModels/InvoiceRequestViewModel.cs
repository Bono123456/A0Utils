using A0Utils.Wpf.Helpers;
using A0Utils.Wpf.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace A0Utils.Wpf.ViewModels
{
    public sealed class InvoiceRequestViewModel : ObservableObject
    {
        private readonly string _licenseNumber;
        private readonly string _appVersion;
        private readonly List<string> _resources;

        public string Email { get; }
        public ReadOnlyCollection<InvoiceRenewalOption> Renewals { get; }
        public bool HasRenewals => Renewals.Count > 0;
        public bool CanSend => _resources.Count > 0 || Renewals.Any(x => x.IsSelected);

        private string _organization = string.Empty;
        public string Organization
        {
            get => _organization;
            set { if (SetProperty(ref _organization, value)) RefreshPreview(); }
        }

        private string _inn = string.Empty;
        public string Inn
        {
            get => _inn;
            set { if (SetProperty(ref _inn, value)) RefreshPreview(); }
        }

        private string _contact = string.Empty;
        public string Contact
        {
            get => _contact;
            set { if (SetProperty(ref _contact, value)) RefreshPreview(); }
        }

        private string _phone = string.Empty;
        public string Phone
        {
            get => _phone;
            set { if (SetProperty(ref _phone, value)) RefreshPreview(); }
        }

        private string _comments = string.Empty;
        public string Comments
        {
            get => _comments;
            set { if (SetProperty(ref _comments, value)) RefreshPreview(); }
        }

        private string _copyButtonText = "Копировать текст";
        public string CopyButtonText
        {
            get => _copyButtonText;
            private set => SetProperty(ref _copyButtonText, value);
        }

        public string Subject
        {
            get
            {
                var parts = new List<string>();
                if (Renewals.Any(x => x.IsSelected)) parts.Add("продление");
                if (_resources.Count > 0) parts.Add("дополнительные ресурсы");
                return parts.Count == 0
                    ? $"Заявка по лицензии {_licenseNumber}"
                    : $"Заявка: {string.Join(" и ", parts)}, лицензия {_licenseNumber}";
            }
        }

        public string Body
        {
            get
            {
                var body = new StringBuilder();
                body.AppendLine("Здравствуйте!");
                body.AppendLine();
                body.AppendLine($"Просим выставить платёжные документы по лицензии № {_licenseNumber}.");

                var selectedRenewals = Renewals.Where(x => x.IsSelected).ToList();
                if (selectedRenewals.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine("Продление:");
                    foreach (var renewal in selectedRenewals) body.AppendLine($"- {renewal.Description}");
                }

                if (_resources.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine("Дополнительные ресурсы:");
                    for (int i = 0; i < _resources.Count; i++) body.AppendLine($"{i + 1}. {_resources[i]}");
                }

                body.AppendLine();
                body.AppendLine($"Организация: {Organization}");
                body.AppendLine($"ИНН: {Inn}");
                body.AppendLine($"Контактное лицо: {Contact}");
                body.AppendLine($"Телефон: {Phone}");
                if (!string.IsNullOrWhiteSpace(Comments))
                {
                    body.AppendLine();
                    body.AppendLine("Комментарий:");
                    body.AppendLine(Comments);
                }
                body.AppendLine();
                body.AppendLine($"Сформировано в программе «Утилиты для А0» {_appVersion}");
                return body.ToString();
            }
        }

        public string ClipboardText => $"Кому: {Email}\r\nТема: {Subject}\r\n\r\n{Body}";
        public string MailtoUri => $"mailto:{Email}?subject={Uri.EscapeDataString(Subject)}&body={Uri.EscapeDataString(Body)}";
        public RelayCommand CopyTextCommand { get; }
        public RelayCommand OpenMailCommand { get; }

        public InvoiceRequestViewModel(string licenseName, LicenseInfoModel licenseInfo,
            IEnumerable<string> resources, string email, string appVersion, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(licenseName)) throw new ArgumentException("License is required.", nameof(licenseName));
            if (licenseInfo == null) throw new ArgumentNullException(nameof(licenseInfo));
            _licenseNumber = Path.GetFileNameWithoutExtension(licenseName);
            _appVersion = appVersion;
            _resources = resources.ToList();
            Email = email;

            var renewals = new List<InvoiceRenewalOption>();
            AddRenewal(renewals, "Продлить сопровождение А0", licenseInfo.A0LicenseExpAt, today);
            AddRenewal(renewals, "Продлить сопровождение ПИР", licenseInfo.PIRLicenseExpAt, today);
            AddRenewal(renewals, "Продлить подписку на базы", licenseInfo.SubscriptionLicenseExpAt, today);
            Renewals = renewals.AsReadOnly();
            foreach (var renewal in Renewals) renewal.PropertyChanged += OnRenewalChanged;

            CopyTextCommand = new RelayCommand(CopyText, () => CanSend);
            OpenMailCommand = new RelayCommand(OpenMail, () => CanSend);
        }

        private static void AddRenewal(List<InvoiceRenewalOption> renewals, string title, DateTime expiresAt, DateTime today)
        {
            if (expiresAt != default) renewals.Add(new InvoiceRenewalOption(title, expiresAt, today));
        }

        private void OnRenewalChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InvoiceRenewalOption.IsSelected)) RefreshPreview();
        }

        private void RefreshPreview()
        {
            // Editable fields are the source of truth; rebuilding never writes back into them.
            OnPropertyChanged(nameof(Subject));
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(CanSend));
            CopyButtonText = "Копировать текст";
            CopyTextCommand.NotifyCanExecuteChanged();
            OpenMailCommand.NotifyCanExecuteChanged();
        }

        private void CopyText()
        {
            if (!CanSend) return;
            try
            {
                Clipboard.SetText(ClipboardText);
                CopyButtonText = "Скопировано";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось скопировать текст в буфер обмена");
                MessageDialogHelper.ShowError("Не удалось скопировать текст. Выделите текст письма и нажмите Ctrl+C.");
            }
        }

        private void OpenMail()
        {
            if (!CanSend) return;
            try
            {
                Process.Start(new ProcessStartInfo(MailtoUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось открыть почтовую программу");
                MessageDialogHelper.ShowError("Не удалось открыть почтовую программу. Нажмите «Копировать текст» и вставьте его в письмо вручную.");
            }
        }
    }
}
