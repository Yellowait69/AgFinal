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

            //  On pré-remplit le nom d'utilisateur avec la session Windows actuelle
            TxtUsername.Text = GetWindowsUsername();

            //  On met directement le curseur dans le champ mot de passe pour faire gagner du temps
            TxtPassword.Focus();
        }

        /// <summary>
        /// Tente de récupérer automatiquement l'identifiant Windows (XA...) de l'utilisateur
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
                MessageBox.Show("Veuillez saisir un nom d'utilisateur et un mot de passe.", "Erreur de saisie", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }


            BtnLogin.IsEnabled = false;
            BtnLogin.Content = "Connexion en cours...";


            Settings.DbConfig.Uid = username;
            Settings.DbConfig.Pwd = password;

            try
            {
                var mfService = new MicroFocusApiService();

                // On teste la connexion sur l'environnement de Dev (D) par défaut
                string envToTest = "D";

                // Appel de la méthode d'authentification asynchrone
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

                MessageBox.Show("Identifiant ou mot de passe incorrect.\nAccès refusé par le serveur Micro Focus.", "Erreur d'authentification", MessageBoxButton.OK, MessageBoxImage.Error);

                TxtPassword.Clear();
                TxtPassword.Focus();


                Settings.DbConfig.Pwd = "";
            }
            catch (Exception ex)
            {

                MessageBox.Show($"Impossible de se connecter au serveur Micro Focus :\n{ex.Message}\n\nVérifiez que votre VPN est bien activé.", "Erreur réseau", MessageBoxButton.OK, MessageBoxImage.Error);

                TxtPassword.Clear();
                TxtPassword.Focus();
                Settings.DbConfig.Pwd = "";
            }
            finally
            {

                BtnLogin.IsEnabled = true;
                BtnLogin.Content = "Se connecter";
            }
        }
    }
}