using System.Windows.Forms;

namespace A0Utils.Wpf.Helpers
{
    public static class MessageDialogHelper
    {
        public static void ShowError(string message, string title = "Утилиты для А0")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void ShowInfo(string message, string title = "Утилиты для А0")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static DialogResult Confirm(string message, string title = "Утилиты для А0")
        {
            return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }
    }
}
