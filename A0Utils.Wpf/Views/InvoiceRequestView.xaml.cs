using System.Windows;

namespace A0Utils.Wpf.Views
{
    public partial class InvoiceRequestView : Window
    {
        public InvoiceRequestView()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
        }
    }
}
