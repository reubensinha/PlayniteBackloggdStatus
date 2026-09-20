using System.Collections.Generic;
using System.Windows;

namespace BackloggdStatus.Views
{
    public class StatusMappingReportRow
    {
        public string GameName  { get; set; }
        public string OldStatus { get; set; }
        public string NewStatus { get; set; }
        public bool   Changed   { get; set; }
    }

    public partial class StatusMappingReportDialog : Window
    {
        public StatusMappingReportDialog(
            string title,
            string gameColumnHeader, string oldColumnHeader, string newColumnHeader,
            List<StatusMappingReportRow> rows)
        {
            InitializeComponent();

            TitleText.Text = title;
            ColGameName.Header  = gameColumnHeader;
            ColOldStatus.Header = oldColumnHeader;
            ColNewStatus.Header = newColumnHeader;

            RowsGrid.ItemsSource = rows ?? new List<StatusMappingReportRow>();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
