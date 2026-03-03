using System.Windows;
using AutoActivator.Config;

namespace AutoActivator.Gui
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            // On pré-remplit le nom d'utilisateur avec celui des Settings
            TxtUsername.Text = Settings.DbConfig.Uid;

            // On met directement le curseur dans le champ mot de passe pour gagner du temps
            TxtPassword.Focus();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password; // Propriété spécifique à la PasswordBox

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Veuillez saisir un nom d'utilisateur et un mot de passe.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. On stocke temporairement les identifiants dans la session
            Settings.DbConfig.Uid = username;
            Settings.DbConfig.Pwd = password;

            // 2. On instancie et on affiche la fenêtre principale
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();

            // 3. On ferme la fenêtre de connexion
            this.Close();
        }
    }
}