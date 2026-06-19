#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BackloggdStatus.Views
{
    public partial class TestResultsDialog : Window
    {
        public List<TestResult> Results { get; }
        public string           Summary { get; }

        public TestResultsDialog(List<TestResult> results)
        {
            Results = results;
            int passed = results.Count(r => r.Passed);
            Summary = $"{passed} / {results.Count} passed";
            InitializeComponent();
            DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
#endif
