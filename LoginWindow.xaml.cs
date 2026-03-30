using System;
using System.DirectoryServices.AccountManagement;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoActivator.Config;
using AutoActivator.Services;

namespace AutoActivator.Gui
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            TxtUsername.Text = GetWindowsUsername();

            TxtPassword.Focus();
        }

        /// <summary>
        /// Attempts to automatically retrieve the user's Windows ID (e.g., XA...)
        /// using the active Directory account.
        /// </summary>
        private string GetWindowsUsername()
        {
            try
            {
                return UserPrincipal.Current.Name.ToUpper();
            }
            catch
            {
                return Settings.DbConfig.Uid ?? "";
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter a username and password.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnLogin.IsEnabled = false;
            BtnLogin.Content = "Connecting...";

            Settings.DbConfig.Uid = username;
            Settings.DbConfig.Pwd = password;

            try
            {
                var mfService = new MicroFocusApiService();

                string envToTest = "D";

                bool isLogged = await mfService.LogonAsync(
                    username,
                    password,
                    envToTest,
                    progressMsg => Console.WriteLine($"[Login] {progressMsg}"),
                    CancellationToken.None
                );

                if (isLogged)
                {
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();

                    this.Close();
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Incorrect username or password.\nAccess denied by the Micro Focus server.", "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);

                TxtPassword.Clear();
                TxtPassword.Focus();

                Settings.DbConfig.Pwd = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to connect to the Micro Focus server:\n{ex.Message}\n\nPlease verify that your VPN is connected.", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);

                TxtPassword.Clear();
                TxtPassword.Focus();
                Settings.DbConfig.Pwd = "";
            }
            finally
            {
                BtnLogin.IsEnabled = true;
                BtnLogin.Content = "Login";
            }
        }
    }
}