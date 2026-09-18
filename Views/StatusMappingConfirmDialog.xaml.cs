using System.Collections.Generic;
using System.Windows;

namespace BackloggdStatus.Views
{
    public class StatusMappingPreviewRow
    {
        public string GameName      { get; set; }
        public string CurrentStatus { get; set; }
        public string NewStatus     { get; set; }
    }

    public partial class StatusMappingConfirmDialog : Window
    {
        public StatusMappingConfirmDialog(
            string title,
            string gameColumnHeader, string currentColumnHeader, string newColumnHeader,
            List<StatusMappingPreviewRow> rows)
        {
            InitializeComponent();

            TitleText.Text = title;
            ColGameName.Header      = gameColumnHeader;
            ColCurrentStatus.Header = currentColumnHeader;
            ColNewStatus.Header     = newColumnHeader;

            rows = rows ?? new List<StatusMappingPreviewRow>();
            RowsGrid.ItemsSource = rows;

            bool hasRows = rows.Count > 0;
            RowsGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
            ApplyButton.IsEnabled = hasRows;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
