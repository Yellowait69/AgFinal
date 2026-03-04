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

            // 1. On pré-remplit le nom d'utilisateur avec la session Windows actuelle
            TxtUsername.Text = GetWindowsUsername();

            // 2. On met directement le curseur dans le champ mot de passe pour faire gagner du temps
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
                // En cas d'échec (ex: hors domaine), on regarde si on a une ancienne valeur sauvée, sinon on laisse vide
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

            // Désactiver le bouton et indiquer le chargement
            BtnLogin.IsEnabled = false;
            BtnLogin.Content = "Connexion en cours...";

            // STOCKAGE EN MÉMOIRE : Très important pour la suite (JCL MFFTP, etc.)
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
                    progressMsg => Console.WriteLine($"[Login] {progressMsg}"), // Suivi optionnel
                    CancellationToken.None
                );

                if (isLogged)
                {
                    // Si c'est OK, on ouvre la fenêtre principale (outil d'extraction/activation)
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();

                    // On ferme cette fenêtre de login
                    this.Close();
                }
            }
            catch (UnauthorizedAccessException)
            {
                // GESTION ANTI-BAN : L'API a renvoyé une erreur 401 ou 403 (Mot de passe faux)
                MessageBox.Show("Identifiant ou mot de passe incorrect.\nAccès refusé par le serveur Micro Focus.", "Erreur d'authentification", MessageBoxButton.OK, MessageBoxImage.Error);

                TxtPassword.Clear();
                TxtPassword.Focus();

                // Par sécurité, on efface le mauvais mot de passe de la mémoire
                Settings.DbConfig.Pwd = "";
            }
            catch (Exception ex)
            {
                // Gestion des autres erreurs (Serveur éteint, VPN coupé, Timeout...)
                MessageBox.Show($"Impossible de se connecter au serveur Micro Focus :\n{ex.Message}\n\nVérifiez que votre VPN est bien activé.", "Erreur réseau", MessageBoxButton.OK, MessageBoxImage.Error);

                TxtPassword.Clear();
                TxtPassword.Focus();
                Settings.DbConfig.Pwd = "";
            }
            finally
            {
                // Quoi qu'il arrive (erreur ou non), si la fenêtre ne s'est pas fermée, on réactive le bouton
                BtnLogin.IsEnabled = true;
                BtnLogin.Content = "Se connecter";
            }
        }
    }
}