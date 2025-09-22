using System.Windows.Forms;

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

            this.cbParametro = new ComboBox();
            this.cbOperador = new ComboBox();
            this.txtValor = new TextBox();
            this.btnAdicionarRegra = new Button();

            this.dgvRegras = new DataGridView();
            this.colId = new DataGridViewTextBoxColumn();
            this.colParametro = new DataGridViewTextBoxColumn();
            this.colOperador = new DataGridViewTextBoxColumn();
            this.colValor = new DataGridViewTextBoxColumn();
            this.colAtiva = new DataGridViewCheckBoxColumn();
            this.colRemover = new DataGridViewButtonColumn();

            this.lvContadores = new ListView();
            this.colIedHdr = new ColumnHeader();
            this.colEventosHdr = new ColumnHeader();

            this.lblTotalEventos = new Label();
            this.txtLog = new TextBox();

            this.timerRelatorio = new System.Windows.Forms.Timer(this.components);
            this.timerMock = new System.Windows.Forms.Timer(this.components);

            ((System.ComponentModel.ISupportInitialize)(this.dgvRegras)).BeginInit();
            this.SuspendLayout();

            // ===== Top row =====
            this.cbParametro.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cbParametro.Location = new System.Drawing.Point(12, 12);
            this.cbParametro.Name = "cbParametro";
            this.cbParametro.Size = new System.Drawing.Size(90, 23);

            this.cbOperador.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cbOperador.Location = new System.Drawing.Point(108, 12);
            this.cbOperador.Name = "cbOperador";
            this.cbOperador.Size = new System.Drawing.Size(60, 23);

            this.txtValor.Location = new System.Drawing.Point(174, 12);
            this.txtValor.Name = "txtValor";
            this.txtValor.Size = new System.Drawing.Size(80, 23);

            this.btnAdicionarRegra.Location = new System.Drawing.Point(262, 12);
            this.btnAdicionarRegra.Name = "btnAdicionarRegra";
            this.btnAdicionarRegra.Size = new System.Drawing.Size(122, 23);
            this.btnAdicionarRegra.Text = "Adicionar Regra";
            this.btnAdicionarRegra.UseVisualStyleBackColor = true;
            this.btnAdicionarRegra.Click += new System.EventHandler(this.btnAdicionarRegra_Click);

            // ===== Rules grid =====
            this.dgvRegras.AllowUserToAddRows = false;
            this.dgvRegras.AllowUserToDeleteRows = false;
            this.dgvRegras.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvRegras.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvRegras.Location = new System.Drawing.Point(12, 45);
            this.dgvRegras.MultiSelect = false;
            this.dgvRegras.Name = "dgvRegras";
            this.dgvRegras.RowHeadersVisible = false;
            this.dgvRegras.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            this.dgvRegras.Size = new System.Drawing.Size(760, 260);

            this.colId.HeaderText = "Id";
            this.colId.Name = "colId";
            this.colId.ReadOnly = true;
            this.colId.FillWeight = 10;

            this.colParametro.HeaderText = "Parâmetro";
            this.colParametro.Name = "colParametro";
            this.colParametro.ReadOnly = true;
            this.colParametro.FillWeight = 25;

            this.colOperador.HeaderText = "Op";
            this.colOperador.Name = "colOperador";
            this.colOperador.ReadOnly = true;
            this.colOperador.FillWeight = 10;

            this.colValor.HeaderText = "Valor";
            this.colValor.Name = "colValor";
            this.colValor.ReadOnly = true;
            this.colValor.FillWeight = 25;

            this.colAtiva.HeaderText = "Ativa";
            this.colAtiva.Name = "colAtiva";
            this.colAtiva.FillWeight = 15;

            this.colRemover.HeaderText = "Remover";
            this.colRemover.Name = "colRemover";
            this.colRemover.Text = "X";
            this.colRemover.UseColumnTextForButtonValue = true;
            this.colRemover.FillWeight = 15;

            this.dgvRegras.Columns.AddRange(new DataGridViewColumn[] {
                this.colId, this.colParametro, this.colOperador, this.colValor, this.colAtiva, this.colRemover
            });

            this.dgvRegras.CellContentClick += new DataGridViewCellEventHandler(this.dgvRegras_CellContentClick);
            this.dgvRegras.CellValueChanged += new DataGridViewCellEventHandler(this.dgvRegras_CellValueChanged);

            // ===== Counters (left bottom) =====
            this.lvContadores.Location = new System.Drawing.Point(12, 315);
            this.lvContadores.Name = "lvContadores";
            this.lvContadores.Size = new System.Drawing.Size(300, 230);
            this.lvContadores.UseCompatibleStateImageBehavior = false;
            this.lvContadores.View = View.Details;
            this.lvContadores.FullRowSelect = true;
            this.lvContadores.Columns.AddRange(new ColumnHeader[] {
                this.colIedHdr, this.colEventosHdr
            });

            this.colIedHdr.Text = "IED";
            this.colIedHdr.Width = 140;
            this.colEventosHdr.Text = "Eventos";
            this.colEventosHdr.Width = 120;

            // ===== Total + Log (right bottom) =====
            this.lblTotalEventos.AutoSize = true;
            this.lblTotalEventos.Location = new System.Drawing.Point(330, 315);
            this.lblTotalEventos.Name = "lblTotalEventos";
            this.lblTotalEventos.Size = new System.Drawing.Size(93, 15);
            this.lblTotalEventos.Text = "Total eventos: 0";

            this.txtLog.Location = new System.Drawing.Point(330, 335);
            this.txtLog.Multiline = true;
            this.txtLog.ScrollBars = ScrollBars.Vertical;
            this.txtLog.Name = "txtLog";
            this.txtLog.Size = new System.Drawing.Size(442, 210);

            // ===== Timers =====
            this.timerRelatorio.Enabled = false;
            this.timerRelatorio.Interval = 500;
            this.timerMock.Enabled = false;
            this.timerMock.Interval = 300;

            // ===== Form =====
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.MinimumSize = new System.Drawing.Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Módulo 4 — Filtro de Eventos";

            this.Controls.Add(this.cbParametro);
            this.Controls.Add(this.cbOperador);
            this.Controls.Add(this.txtValor);
            this.Controls.Add(this.btnAdicionarRegra);

            this.Controls.Add(this.dgvRegras);

            this.Controls.Add(this.lvContadores);
            this.Controls.Add(this.lblTotalEventos);
            this.Controls.Add(this.txtLog);

            ((System.ComponentModel.ISupportInitialize)(this.dgvRegras)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private ComboBox cbParametro;
        private ComboBox cbOperador;
        private TextBox txtValor;
        private Button btnAdicionarRegra;

        private DataGridView dgvRegras;
        private DataGridViewTextBoxColumn colId;
        private DataGridViewTextBoxColumn colParametro;
        private DataGridViewTextBoxColumn colOperador;
        private DataGridViewTextBoxColumn colValor;
        private DataGridViewCheckBoxColumn colAtiva;
        private DataGridViewButtonColumn colRemover;

        private ListView lvContadores;
        private ColumnHeader colIedHdr;
        private ColumnHeader colEventosHdr;

        private Label lblTotalEventos;
        private TextBox txtLog;

        private System.Windows.Forms.Timer timerRelatorio;
        private System.Windows.Forms.Timer timerMock;
    }
}
