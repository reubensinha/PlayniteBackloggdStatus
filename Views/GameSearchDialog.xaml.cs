using Playnite.SDK;
using System.Collections.Generic;
using System.Windows;

namespace BackloggdStatus.Views
{
    public partial class GameSearchDialog : Window
    {
        public BackloggdSearchResult Result { get; private set; }

        public GameSearchDialog(string gameName, List<BackloggdSearchResult> results)
        {
            InitializeComponent();
            DataContext = new GameSearchDialogViewModel(gameName, results);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Result = ((GameSearchDialogViewModel)DataContext).SelectedResult;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class GameSearchDialogViewModel : ObservableObject
    {
        public string SearchTitle     { get; }
        public string ResultCountText { get; }
        public List<BackloggdSearchResult> SearchResults { get; }

        private BackloggdSearchResult _selected;
        public BackloggdSearchResult SelectedResult
        {
            get => _selected;
            set
            {
                SetValue(ref _selected, value);
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => SelectedResult != null;

        public GameSearchDialogViewModel(string gameName, List<BackloggdSearchResult> results)
        {
            SearchTitle     = $"Search results for: {gameName}";
            SearchResults   = results ?? new List<BackloggdSearchResult>();
            ResultCountText = $"{SearchResults.Count} result(s)";

            if (SearchResults.Count > 0)
                SelectedResult = SearchResults[0];
        }
    }
}
