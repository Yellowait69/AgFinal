using System.Diagnostics;
using System.IO;
using System.Windows;
using AutoActivator.Services; // Ton ancienne logique
using AutoActivator.Config;

private string _lastGeneratedPath = "";

private void BtnRun_Click(object sender, RoutedEventArgs e)
{
    string contract = TxtContract.Text.Trim();
    if (string.IsNullOrEmpty(contract)) return;

    // Ici on appelle ta logique existante (adaptée de Program.cs)
    try {
        string fileName = $"FULL_EXTRACT_{contract}.csv";
        _lastGeneratedPath = Path.Combine(Settings.OutputDir, fileName);

        // Appel de ta méthode d'extraction (il faudra peut-être la rendre publique dans Program.cs ou la déplacer)
        // Pour l'exemple, on simule la réussite :
        TxtStatus.Text = $"Extraction terminée avec succès !";
        TxtStatus.Foreground = System.Windows.Media.Brushes.Green;

        // On affiche le lien
        LnkFile.Visibility = Visibility.Visible;
    }
    catch (Exception ex) {
        TxtStatus.Text = $"Erreur : {ex.Message}";
        TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
    }
}

// Action au clic sur le lien "Ouvrir le fichier"
private void Hyperlink_Click(object sender, RoutedEventArgs e)
{
    if (File.Exists(_lastGeneratedPath))
    {
        // Cette commande ouvre le fichier avec l'application par défaut (Excel/Notepad)
        // Et sélectionne le fichier dans le dossier
        Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
    }
}