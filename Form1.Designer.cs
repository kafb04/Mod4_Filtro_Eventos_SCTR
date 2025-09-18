namespace Modulo4FiltroEventosWinForms
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.cbParametro = new System.Windows.Forms.ComboBox();
            this.cbOperador = new System.Windows.Forms.ComboBox();
            this.cbIed = new System.Windows.Forms.ComboBox();
            this.txtValor = new System.Windows.Forms.TextBox();
            this.btnAdicionarRegra = new System.Windows.Forms.Button();

            this.dgvRegras = new System.Windows.Forms.DataGridView();
            this.colId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colParametro = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colOperador = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colValor = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colIed = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colAtiva = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colRemover = new System.Windows.Forms.DataGridViewButtonColumn();

            this.dgvPacotes = new System.Windows.Forms.DataGridView();
            this.colPktTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPktIed = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPktIA = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPktIB = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPktIC = new System.Windows.Forms.DataGridViewTextBoxColumn();

            this.lvContadores = new System.Windows.Forms.ListView();
            this.colIedHdr = new System.Windows.Forms.ColumnHeader();
            this.colEventosHdr = new System.Windows.Forms.ColumnHeader();

            this.lblTotalEventos = new System.Windows.Forms.Label();
            this.txtLog = new System.Windows.Forms.TextBox();

            this.timerRelatorio = new System.Windows.Forms.Timer(this.components);
            this.timerMock = new System.Windows.Forms.Timer(this.components);

            ((System.ComponentModel.ISupportInitialize)(this.dgvRegras)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvPacotes)).BeginInit();
            this.SuspendLayout();

            // ===== Combos & Inputs (linha superior) =====
            this.cbParametro.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbParametro.Location = new System.Drawing.Point(12, 12);
            this.cbParametro.Name = "cbParametro";
            this.cbParametro.Size = new System.Drawing.Size(90, 23);

            this.cbOperador.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbOperador.Location = new System.Drawing.Point(108, 12);
            this.cbOperador.Name = "cbOperador";
            this.cbOperador.Size = new System.Drawing.Size(60, 23);

            this.txtValor.Location = new System.Drawing.Point(174, 12);
            this.txtValor.Name = "txtValor";
            this.txtValor.Size = new System.Drawing.Size(80, 23);

            this.cbIed.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbIed.Location = new System.Drawing.Point(260, 12);
            this.cbIed.Name = "cbIed";
            this.cbIed.Size = new System.Drawing.Size(100, 23);

            this.btnAdicionarRegra.Location = new System.Drawing.Point(366, 12);
            this.btnAdicionarRegra.Name = "btnAdicionarRegra";
            this.btnAdicionarRegra.Size = new System.Drawing.Size(122, 23);
            this.btnAdicionarRegra.Text = "Adicionar Regra";
            this.btnAdicionarRegra.UseVisualStyleBackColor = true;
            this.btnAdicionarRegra.Click += new System.EventHandler(this.btnAdicionarRegra_Click);

            // ===== Grid de Regras =====
            this.dgvRegras.AllowUserToAddRows = false;
            this.dgvRegras.AllowUserToDeleteRows = false;
            this.dgvRegras.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvRegras.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvRegras.Location = new System.Drawing.Point(12, 45);
            this.dgvRegras.MultiSelect = false;
            this.dgvRegras.Name = "dgvRegras";
            this.dgvRegras.RowHeadersVisible = false;
            this.dgvRegras.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvRegras.Size = new System.Drawing.Size(760, 150);

            this.colId.HeaderText = "Id";
            this.colId.Name = "colId";
            this.colId.ReadOnly = true;
            this.colId.FillWeight = 10;

            this.colParametro.HeaderText = "Parâmetro";
            this.colParametro.Name = "colParametro";
            this.colParametro.ReadOnly = true;
            this.colParametro.FillWeight = 20;

            this.colOperador.HeaderText = "Op";
            this.colOperador.Name = "colOperador";
            this.colOperador.ReadOnly = true;
            this.colOperador.FillWeight = 10;

            this.colValor.HeaderText = "Valor";
            this.colValor.Name = "colValor";
            this.colValor.ReadOnly = true;
            this.colValor.FillWeight = 20;

            this.colIed.HeaderText = "IED";
            this.colIed.Name = "colIed";
            this.colIed.ReadOnly = true;
            this.colIed.FillWeight = 20;

            this.colAtiva.HeaderText = "Ativa";
            this.colAtiva.Name = "colAtiva";
            this.colAtiva.FillWeight = 10;

            this.colRemover.HeaderText = "Remover";
            this.colRemover.Name = "colRemover";
            this.colRemover.Text = "X";
            this.colRemover.UseColumnTextForButtonValue = true;
            this.colRemover.FillWeight = 10;

            this.dgvRegras.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colId, this.colParametro, this.colOperador, this.colValor, this.colIed, this.colAtiva, this.colRemover
            });

            this.dgvRegras.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvRegras_CellContentClick);
            this.dgvRegras.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvRegras_CellValueChanged);

            // ===== Grid de Pacotes (só correntes) =====
            this.dgvPacotes.AllowUserToAddRows = false;
            this.dgvPacotes.AllowUserToDeleteRows = false;
            this.dgvPacotes.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvPacotes.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvPacotes.Location = new System.Drawing.Point(12, 205);
            this.dgvPacotes.MultiSelect = false;
            this.dgvPacotes.Name = "dgvPacotes";
            this.dgvPacotes.ReadOnly = true;
            this.dgvPacotes.RowHeadersVisible = false;
            this.dgvPacotes.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvPacotes.Size = new System.Drawing.Size(760, 170);

            this.colPktTime.HeaderText = "Hora";
            this.colPktTime.Name = "colPktTime";
            this.colPktTime.ReadOnly = true;
            this.colPktTime.FillWeight = 20;

            this.colPktIed.HeaderText = "IED";
            this.colPktIed.Name = "colPktIed";
            this.colPktIed.ReadOnly = true;
            this.colPktIed.FillWeight = 20;

            this.colPktIA.HeaderText = "IA";
            this.colPktIA.Name = "colPktIA";
            this.colPktIA.ReadOnly = true;
            this.colPktIA.FillWeight = 20;

            this.colPktIB.HeaderText = "IB";
            this.colPktIB.Name = "colPktIB";
            this.colPktIB.ReadOnly = true;
            this.colPktIB.FillWeight = 20;

            this.colPktIC.HeaderText = "IC";
            this.colPktIC.Name = "colPktIC";
            this.colPktIC.ReadOnly = true;
            this.colPktIC.FillWeight = 20;

            this.dgvPacotes.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colPktTime, this.colPktIed, this.colPktIA, this.colPktIB, this.colPktIC
            });

            // ===== ListView de contadores por IED =====
            this.lvContadores.Location = new System.Drawing.Point(12, 385);
            this.lvContadores.Name = "lvContadores";
            this.lvContadores.Size = new System.Drawing.Size(300, 170);
            this.lvContadores.UseCompatibleStateImageBehavior = false;
            this.lvContadores.View = System.Windows.Forms.View.Details;
            this.lvContadores.FullRowSelect = true;
            this.lvContadores.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.colIedHdr, this.colEventosHdr
            });

            this.colIedHdr.Text = "IED";
            this.colIedHdr.Width = 140;
            this.colEventosHdr.Text = "Eventos";
            this.colEventosHdr.Width = 120;

            // ===== Label total de eventos =====
            this.lblTotalEventos.AutoSize = true;
            this.lblTotalEventos.Location = new System.Drawing.Point(330, 385);
            this.lblTotalEventos.Name = "lblTotalEventos";
            this.lblTotalEventos.Size = new System.Drawing.Size(93, 15);
            this.lblTotalEventos.Text = "Total eventos: 0";

            // ===== TextBox de Log (JSON gerado automaticamente) =====
            this.txtLog.Location = new System.Drawing.Point(470, 385);
            this.txtLog.Multiline = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = false;
            this.txtLog.Size = new System.Drawing.Size(302, 170);

            // ===== Timers =====
            this.timerRelatorio.Enabled = false; // ligado no Form1.cs
            this.timerRelatorio.Interval = 500;

            this.timerMock.Enabled = false;     // ligado no Form1.cs
            this.timerMock.Interval = 300;

            // ===== Form =====
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 571);
            this.MinimumSize = new System.Drawing.Size(800, 610);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Módulo 4 — Filtro de Eventos (Correntes)";

            this.Controls.Add(this.cbParametro);
            this.Controls.Add(this.cbOperador);
            this.Controls.Add(this.txtValor);
            this.Controls.Add(this.cbIed);
            this.Controls.Add(this.btnAdicionarRegra);

            this.Controls.Add(this.dgvRegras);
            this.Controls.Add(this.dgvPacotes);

            this.Controls.Add(this.lvContadores);
            this.Controls.Add(this.lblTotalEventos);
            this.Controls.Add(this.txtLog);

            ((System.ComponentModel.ISupportInitialize)(this.dgvRegras)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvPacotes)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        // Controles de UI usados no Form1.cs
        private System.Windows.Forms.ComboBox cbParametro;
        private System.Windows.Forms.ComboBox cbOperador;
        private System.Windows.Forms.ComboBox cbIed;
        private System.Windows.Forms.TextBox txtValor;
        private System.Windows.Forms.Button btnAdicionarRegra;

        private System.Windows.Forms.DataGridView dgvRegras;
        private System.Windows.Forms.DataGridViewTextBoxColumn colId;
        private System.Windows.Forms.DataGridViewTextBoxColumn colParametro;
        private System.Windows.Forms.DataGridViewTextBoxColumn colOperador;
        private System.Windows.Forms.DataGridViewTextBoxColumn colValor;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIed;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colAtiva;
        private System.Windows.Forms.DataGridViewButtonColumn colRemover;

        private System.Windows.Forms.DataGridView dgvPacotes;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPktTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPktIed;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPktIA;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPktIB;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPktIC;

        private System.Windows.Forms.ListView lvContadores;
        private System.Windows.Forms.ColumnHeader colIedHdr;
        private System.Windows.Forms.ColumnHeader colEventosHdr;

        private System.Windows.Forms.Label lblTotalEventos;
        private System.Windows.Forms.TextBox txtLog;

        private System.Windows.Forms.Timer timerRelatorio;
        private System.Windows.Forms.Timer timerMock;
    }
}
