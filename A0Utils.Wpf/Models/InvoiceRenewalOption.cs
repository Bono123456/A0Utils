using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace A0Utils.Wpf.Models
{
    public sealed class InvoiceRenewalOption : ObservableObject
    {
        public string Description { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public InvoiceRenewalOption(string title, DateTime expiresAt, DateTime today)
        {
            var status = expiresAt.Date < today.Date
                ? $"срок закончился {expiresAt:dd.MM.yyyy}"
                : $"действует до {expiresAt:dd.MM.yyyy}";
            Description = $"{title} ({status})";
            _isSelected = (expiresAt.Date - today.Date).TotalDays <= 30;
        }
    }
}
