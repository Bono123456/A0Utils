using A0Utils.Wpf.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace A0Utils.Wpf.Converters
{
    public sealed class GroupExpandConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Hidden grouping levels still contain groups, so descend to a leaf.
            while (value is CollectionViewGroup group)
            {
                value = group.Items.FirstOrDefault();
            }

            if (value is not UpdateModel update)
            {
                return false;
            }

            switch (parameter as string)
            {
                case nameof(UpdateGroupField.Region):
                    return update.IsOnlyRegion;
                case nameof(UpdateGroupField.Year):
                    return update.IsNewestYear;
                default:
                    return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
