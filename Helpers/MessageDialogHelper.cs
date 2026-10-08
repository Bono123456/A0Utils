using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public static void Show(
            Func<IList<RenewalOption>, string> buildSubject,
            Func<IList<RenewalOption>, string> buildBody,
            IList<RenewalOption> renewals,
            string email)
        {
            var window = new System.Windows.Window
            {
                Title = "Запрос счёта",
                Width = 560,
                Height = 560,
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
                Text = $"Письмо для {email}. Отметьте, что нужно продлить, затем допишите реквизиты организации и отправьте.\n" +
                       "Если почтовая программа не открылась, скопируйте текст и вставьте в письмо вручную.",
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            System.Windows.Controls.DockPanel.SetDock(hint, System.Windows.Controls.Dock.Top);
            root.Children.Add(hint);

            // Галочки продления. Отмечены заранее, если срок подходит к концу или уже закончился.
            // При изменении галочек тема и текст письма собираются заново.
            var renewalPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            System.Windows.Controls.DockPanel.SetDock(renewalPanel, System.Windows.Controls.Dock.Top);
            if (renewals.Count > 0)
            {
                renewalPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Продление (если требуется):",
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Margin = new System.Windows.Thickness(0, 0, 0, 4)
                });
            }
            root.Children.Add(renewalPanel);

            var subjectBox = new System.Windows.Controls.TextBox
            {
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
                AcceptsReturn = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Padding = new System.Windows.Thickness(4)
            };
            root.Children.Add(bodyBox);

            void Rebuild()
            {
                var chosen = renewals.Where(x => x.IsChecked).ToList();
                subjectBox.Text = buildSubject(chosen);
                bodyBox.Text = buildBody(chosen);
            }

            foreach (var renewal in renewals)
            {
                var option = renewal;
                var checkBox = new System.Windows.Controls.CheckBox
                {
                    Content = option.Title,
                    IsChecked = option.IsChecked,
                    Margin = new System.Windows.Thickness(0, 2, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                checkBox.Checked += (s, e) => { option.IsChecked = true; Rebuild(); };
                checkBox.Unchecked += (s, e) => { option.IsChecked = false; Rebuild(); };
                renewalPanel.Children.Add(checkBox);
            }

            Rebuild();

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

    // Пункт продления в заявке: А0, ПИР или подписка на базы
    public sealed class RenewalOption
    {
        public string Title { get; set; }
        public string EmailLine { get; set; }
        public bool IsChecked { get; set; }
    }
}
