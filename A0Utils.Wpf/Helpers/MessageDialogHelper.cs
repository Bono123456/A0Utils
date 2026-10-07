using Serilog;
using System;
using System.Diagnostics;
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

    // Окно с текстом запроса счёта: текст можно скопировать или открыть письмо в почтовой программе.
    // Окно собирается в коде, чтобы не добавлять в проект отдельный файл.
    public static class RequestDialogHelper
    {
        public static void Show(string subject, string body, string email)
        {
            var window = new System.Windows.Window
            {
                Title = "Запрос счёта",
                Width = 560,
                Height = 480,
                MinWidth = 400,
                MinHeight = 300,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = System.Windows.Application.Current?.MainWindow,
                ShowInTaskbar = false,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 12,
                Background = (System.Windows.Media.Brush)System.Windows.Application.Current?.TryFindResource("WindowBrush")
                             ?? System.Windows.Media.Brushes.WhiteSmoke
            };

            var root = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(14) };

            var hint = new System.Windows.Controls.TextBlock
            {
                Text = $"Письмо для {email}. Допишите реквизиты организации и отправьте.\n" +
                       "Если почтовая программа не открылась, скопируйте текст и вставьте в письмо вручную.",
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            System.Windows.Controls.DockPanel.SetDock(hint, System.Windows.Controls.Dock.Top);
            root.Children.Add(hint);

            var subjectBox = new System.Windows.Controls.TextBox
            {
                Text = subject,
                Padding = new System.Windows.Thickness(4),
                Margin = new System.Windows.Thickness(0, 0, 0, 6)
            };
            System.Windows.Controls.DockPanel.SetDock(subjectBox, System.Windows.Controls.Dock.Top);
            root.Children.Add(subjectBox);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(0, 10, 0, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
            root.Children.Add(buttons);

            var bodyBox = new System.Windows.Controls.TextBox
            {
                Text = body,
                AcceptsReturn = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Padding = new System.Windows.Thickness(4)
            };
            root.Children.Add(bodyBox);

            var copyButton = new System.Windows.Controls.Button { Content = "Копировать текст", Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            copyButton.Click += (s, e) =>
            {
                try
                {
                    System.Windows.Clipboard.SetText($"Кому: {email}\r\nТема: {subjectBox.Text}\r\n\r\n{bodyBox.Text}");
                    copyButton.Content = "Скопировано";
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Не удалось скопировать текст в буфер обмена");
                    MessageDialogHelper.ShowError("Не удалось скопировать текст. Выделите его и нажмите Ctrl+C.");
                }
            };

            var mailButton = new System.Windows.Controls.Button { Content = "Открыть в почте", Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            // Оформление кнопок берём из главного окна
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            var buttonStyle = mainWindow?.TryFindResource(typeof(System.Windows.Controls.Button)) as System.Windows.Style;
            var primaryStyle = mainWindow?.TryFindResource("PrimaryButton") as System.Windows.Style;
            if (buttonStyle != null)
            {
                copyButton.Style = buttonStyle;
            }
            if (primaryStyle != null)
            {
                mailButton.Style = primaryStyle;
            }
            mailButton.Click += (s, e) =>
            {
                try
                {
                    var mailto = $"mailto:{email}?subject={Uri.EscapeDataString(subjectBox.Text)}&body={Uri.EscapeDataString(bodyBox.Text)}";
                    Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Не удалось открыть почтовую программу");
                    MessageDialogHelper.ShowError("Не удалось открыть почтовую программу. Нажмите «Копировать текст» и вставьте его в письмо вручную.");
                }
            };

            var closeButton = new System.Windows.Controls.Button { Content = "Закрыть", IsCancel = true };
            if (buttonStyle != null)
            {
                closeButton.Style = buttonStyle;
            }
            closeButton.Click += (s, e) => window.Close();

            buttons.Children.Add(copyButton);
            buttons.Children.Add(mailButton);
            buttons.Children.Add(closeButton);

            window.Content = root;
            window.ShowDialog();
        }
    }
}
