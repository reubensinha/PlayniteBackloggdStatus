using System;
using System.Windows;
using System.Windows.Controls;

namespace BackloggdStatus
{
    public partial class BackloggdStatusSettingsView : UserControl
    {
        private BackloggdStatusSettingsViewModel ViewModel =>
            DataContext as BackloggdStatusSettingsViewModel;

        public BackloggdStatusSettingsView()
        {
            InitializeComponent();
#if DEBUG
            RunTestsButton.Visibility = Visibility.Visible;
#endif
        }

        private void SignIn_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnSignInRequested?.Invoke();

        private void SignOut_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnSignOutRequested?.Invoke();

        private void SyncAll_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnSyncAllRequested?.Invoke();

        private void ApplyStatusMappings_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnApplyStatusMappingsRequested?.Invoke();

        private void PullFromBackloggd_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnPullFromBackloggdRequested?.Invoke();

        private void OpenLog_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnOpenLogRequested?.Invoke();

        private void Unlink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Guid id)
                ViewModel?.OnUnlinkRequested?.Invoke(id);
        }

        // SelectedItem/SelectedValue two-way bindings on ComboBoxes embedded in a
        // DataGridTemplateColumn.CellTemplate don't reliably write back to the source here
        // (confirmed via diagnostic logging — the property setter never fired despite the
        // ComboBox visually holding the selection). Writing back explicitly on SelectionChanged
        // sidesteps that, matching the same escape-hatch pattern Unlink_Click already uses.
        private void TriState_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox combo) || !(combo.DataContext is CompletionStatusMapping row) || !(combo.SelectedItem is TriState value))
                return;
            switch (combo.Tag as string)
            {
                case "Playing":  row.Playing  = value; break;
                case "Backlog":  row.Backlog  = value; break;
                case "Wishlist": row.Wishlist = value; break;
            }
        }

        private void PlayedTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.DataContext is CompletionStatusMapping row && combo.SelectedItem is PlayedTargetState value)
                row.Played = value;
        }

        private void PullMappingTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.DataContext is BackloggdStatusMapping row && combo.SelectedItem is CompletionStatusOption option)
                row.TargetCompletionStatusId = option.Id;
        }

        private void CollectDiagnostics_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnCollectDiagnosticsRequested?.Invoke();

        private void RunTestsButton_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnRunTestsRequested?.Invoke();
    }
}
