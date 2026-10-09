using A0Utils.Wpf.Models;
using System;
using System.ComponentModel;
using System.Globalization;

namespace A0Utils.Wpf.Converters
{
    public enum UpdateGroupField
    {
        Tag,
        Region,
        Year
    }

    // Keeps category-specific presentation rules out of the update model.
    public sealed class UpdateGroupDescription : GroupDescription
    {
        public UpdateGroupField Field { get; set; }

        public override object GroupNameFromItem(object item, int level, CultureInfo culture)
        {
            if (item is not UpdateModel update)
            {
                return string.Empty;
            }

            switch (Field)
            {
                case UpdateGroupField.Tag:
                    if (string.IsNullOrWhiteSpace(update.Tag))
                    {
                        // Keep untagged resources separate from the unheaded latest versions.
                        return " ";
                    }

                    return update.IsLatestInTag ? string.Empty : $"{update.Tag}: предыдущие изменения";

                case UpdateGroupField.Region:
                    return update.Category == "Справочники цен" && string.IsNullOrWhiteSpace(update.Tag)
                        ? (string.IsNullOrWhiteSpace(update.Region) ? "Прочие" : update.Region)
                        : string.Empty;

                case UpdateGroupField.Year:
                    return update.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

                default:
                    throw new ArgumentOutOfRangeException(nameof(Field));
            }
        }
    }
}
