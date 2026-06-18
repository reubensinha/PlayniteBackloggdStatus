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

        private void OpenLog_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnOpenLogRequested?.Invoke();

        private void Unlink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Guid id)
                ViewModel?.OnUnlinkRequested?.Invoke(id);
        }

        private void RunTestsButton_Click(object sender, RoutedEventArgs e)
            => ViewModel?.OnRunTestsRequested?.Invoke();
    }
}
