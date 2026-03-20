using System.Windows.Controls;

namespace AutoActivator.Gui.Views
{
    /// <summary>
    /// Interaction logic for HelpView.xaml.
    /// This view contains the static documentation and user guides divided into tabs
    /// for each application module (Extraction, Activation, and Comparison),
    /// including formatting rules, templates, and troubleshooting instructions.
    /// </summary>
    public partial class HelpView : UserControl
    {
        public HelpView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Allows navigating directly to a specific tab from another module.
        /// 0 = Extraction, 1 = Activation, 2 = Comparison
        /// </summary>
        /// <param name="index">The index of the tab to select.</param>
        public void SelectTab(int index)
        {
            // Vérifie que l'index demandé existe bien dans le TabControl pour éviter un crash
            if (HelpTabControl != null && index >= 0 && index < HelpTabControl.Items.Count)
            {
                HelpTabControl.SelectedIndex = index;
            }
        }
    }
}