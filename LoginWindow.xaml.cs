using System;
using System.Threading.Tasks;
using System.Windows;
using AutoActivator.Config;
using AutoActivator.Services; // Ajout nécessaire pour utiliser DatabaseManager

namespace AutoActivator.Gui
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            // On pré-remplit le nom d'utilisateur avec celui des Settings
            TxtUsername.Text = Settings.DbConfig.Uid ?? "";

            // On met directement le curseur dans le champ mot de passe pour gagner du temps
            TxtPassword.Focus();
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password; // Propriété spécifique à la PasswordBox

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Veuillez saisir un nom d'utilisateur et un mot de passe.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Désactiver le bouton pendant la tentative de connexion
            BtnLogin.IsEnabled = false;
            BtnLogin.Content = "Connexion...";

            // 1. On stocke temporairement les identifiants dans la configuration.
            // Le DatabaseManager s'en servira via Settings.DbConfig.GetConnectionString()
            Settings.DbConfig.Uid = username;
            Settings.DbConfig.Pwd = password;

            try
            {
                // 2. On tente la connexion à la base de données de manière asynchrone
                bool isLogged = await Task.Run(() =>
                {
                    // On instancie le gestionnaire de base de données.
                    // "D" est utilisé par défaut pour l'environnement (Dev). Modifiez si besoin ("Q", "P", etc.)
                    var dbManager = new DatabaseManager("D");
                    return dbManager.TestConnection();
                });

                if (isLogged)
                {
                    // 3. Si la connexion est réussie, on ouvre la fenêtre principale
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();

                    // 4. On ferme la fenêtre de connexion
                    this.Close();
                }
                else
                {
                    // 5. En cas d'échec (mauvais mdp ou serveur indisponible)
                    MessageBox.Show("Identifiant ou mot de passe incorrect, ou base de données LISA inaccessible.", "Erreur de connexion", MessageBoxButton.OK, MessageBoxImage.Error);

                    // On nettoie le mot de passe pour que l'utilisateur puisse réessayer
                    TxtPassword.Clear();
                    TxtPassword.Focus();

                    // Par sécurité, on retire le faux mot de passe des paramètres
                    Settings.DbConfig.Pwd = "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Une erreur inattendue est survenue : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // On réactive le bouton (surtout utile si la connexion a échoué pour pouvoir réessayer)
                BtnLogin.IsEnabled = true;
                BtnLogin.Content = "Se connecter";
            }
        }
    }
}