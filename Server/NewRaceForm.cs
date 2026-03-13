using LEGORacersAPI;
using System;
using System.Windows.Forms;

namespace Server
{
    public partial class NewRaceForm : Form
    {
        public Circuit SelectedCircuit { get; set; }

        public bool Mirror { get; set; }

        public NewRaceForm()
        {
            InitializeComponent();

            cmbCircuit.DataSource = Circuit.GetAll();
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            SelectedCircuit = (Circuit)cmbCircuit.SelectedValue;
            Mirror = chkMirror.Checked;

            Close();
        }
    }
}