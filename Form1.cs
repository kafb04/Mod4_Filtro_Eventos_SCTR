using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets; 
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Modulo4FiltroEventosWinForms
{
    public enum Operador { Menor, Igual, Maior }

    public record Medida(DateTime Timestamp, string IedId, double IA, double IB, double IC);

    public class Regra
    {
        public int Id { get; init; }
        public string Parametro { get; set; } = "IA";
        public Operador Operador { get; set; } = Operador.Maior;
        public double Valor { get; set; } = 300.0;
        public string IedFiltro { get; set; } = "Todos"; 
        public bool Ativa { get; set; } = true;

        public bool Verifica(Medida m)
        {
            if (!Ativa) return false;
            if (IedFiltro != "Todos" && m.IedId != IedFiltro) return false;

            double x = Parametro switch
            {
                "IA" => m.IA,
                "IB" => m.IB,
                "IC" => m.IC,
                _ => double.NaN
            };

            return Operador switch
            {
                Operador.Maior => x > Valor,
                Operador.Igual => Math.Abs(x - Valor) < 1e-6,
                Operador.Menor => x < Valor,
                _ => false
            };
        }
    }

    public partial class Form1 : Form
    {
        // ====== UDP ======
        // Entrada (pacotes do Módulo 1 – simulador)
        private readonly int _portaRecepcao = 5002; 
        private UdpClient? _udpRecv;

        // Saída (eventos para Módulo 3)
        private const string Modulo3Host = "127.0.0.1";
        private const int Modulo3Porta = 6001;
        private UdpClient? _udpSend;

        // ====== Estruturas ======
        private readonly List<Regra> _regras = new();
        private int _nextRegraId = 1;

        // Contadores para UI
        private readonly ConcurrentDictionary<string, int> _contadoresPorIed = new();
        private int _totalEventos = 0;

        // Máquina de estados: (regraId, IED) -> ativo?
        private readonly ConcurrentDictionary<(int rid, string ied), bool> _estadoAnterior = new();

        // ====== Threads & Filas ======
        private System.Threading.CancellationTokenSource _cts = new();
        private System.Threading.Thread? _rxThread;
        private System.Threading.Thread? _procThread;
        private System.Threading.Thread? _txThread;

        private readonly ConcurrentQueue<string> _rxQueue = new();  
        private readonly ConcurrentQueue<byte[]> _txQueue = new();  

        // ====== Rede: timeout / backoff ======
        private readonly TimeSpan _rxInatividade = TimeSpan.FromSeconds(2); 
        private System.Diagnostics.Stopwatch _rxWatch = new System.Diagnostics.Stopwatch();
        private int _rxErroSeq = 0;
        private const int _rxErroSeqMax = 5;

        private int _txErroSeq = 0;
        private const int _txRetryMax = 3;

        // ====== Mock & Log ======
        private readonly Random _rng = new();
        private string _ultimoPacoteJson = "{\n}";
        private const bool USE_MOCK = false; 

        public Form1()
        {
            InitializeComponent();
            InicializarCombos();

            // Timers (UI)
            timerRelatorio.Interval = 500;                 
            timerRelatorio.Tick += timerRelatorio_Tick;
            timerRelatorio.Start();

            // MOCK (desligado por padrão; ligue se quiser testar sem simulador)
            timerMock.Interval = 300;
            if (USE_MOCK)
                timerMock.Tick += (_, __) => GerarMedidaMock();

            // UDP TX
            try { _udpSend = new UdpClient(); } catch { _udpSend = null; }

            // UDP RX (socket bloqueante)
            CriarSocketRx();

            // Inicia workers (RX / PROC / TX)
            _cts = new System.Threading.CancellationTokenSource();
            _rxThread = new System.Threading.Thread(() => RxLoop(_cts.Token)) { IsBackground = true, Name = "RX" };
            _procThread = new System.Threading.Thread(() => ProcLoop(_cts.Token)) { IsBackground = true, Name = "PROC" };
            _txThread = new System.Threading.Thread(() => TxLoop(_cts.Token)) { IsBackground = true, Name = "TX" };
            _rxThread.Start();
            _procThread.Start();
            _txThread.Start();

            _rxWatch.Start();

            // Fechamento limpo
            this.FormClosing += (s, e) =>
            {
                try
                {
                    _cts.Cancel();
                    System.Threading.Thread.Sleep(50);
                    _udpRecv?.Close(); _udpRecv?.Dispose();
                    _udpSend?.Close(); _udpSend?.Dispose();
                }
                catch { }
            };
        }

        private void InicializarCombos()
        {
            cbParametro.Items.Clear();
            cbParametro.Items.AddRange(new object[] { "IA", "IB", "IC" });
            cbParametro.SelectedIndex = 0;

            cbOperador.Items.Clear();
            cbOperador.Items.AddRange(new object[] { "<", "=", ">" });
            cbOperador.SelectedIndex = 2;

            cbIed.Items.Clear();
            cbIed.Items.AddRange(new object[] { "Todos", "IED1", "IED2", "IED3" });
            cbIed.SelectedIndex = 0;
        }

        private Operador ParseOperador(string symbol) =>
            symbol switch { "<" => Operador.Menor, "=" => Operador.Igual, ">" => Operador.Maior, _ => Operador.Maior };

        private void btnAdicionarRegra_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(
                    txtValor.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val))
            {
                MessageBox.Show("Valor inválido.");
                return;
            }

            var r = new Regra
            {
                Id = _nextRegraId++,
                Parametro = cbParametro.SelectedItem?.ToString() ?? "IA",
                Operador = ParseOperador(cbOperador.SelectedItem?.ToString() ?? ">"),
                Valor = val,
                IedFiltro = cbIed.SelectedItem?.ToString() ?? "Todos",
                Ativa = true
            };

            _regras.Add(r);
            AdicionarRegraNaGrid(r);
        }

        private void AdicionarRegraNaGrid(Regra r)
        {
            int row = dgvRegras.Rows.Add();
            var rr = dgvRegras.Rows[row];
            rr.Cells["colId"].Value = r.Id;
            rr.Cells["colParametro"].Value = r.Parametro;
            rr.Cells["colOperador"].Value = r.Operador switch { Operador.Maior => ">", Operador.Igual => "=", _ => "<" };
            rr.Cells["colValor"].Value = r.Valor;
            rr.Cells["colIed"].Value = r.IedFiltro;
            rr.Cells["colAtiva"].Value = r.Ativa;
        }

        private void dgvRegras_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgvRegras.Columns[e.ColumnIndex].Name == "colRemover")
            {
                int id = Convert.ToInt32(dgvRegras.Rows[e.RowIndex].Cells["colId"].Value);
                _regras.RemoveAll(x => x.Id == id);
                dgvRegras.Rows.RemoveAt(e.RowIndex);
            }
        }

        private void dgvRegras_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            int id = Convert.ToInt32(dgvRegras.Rows[e.RowIndex].Cells["colId"].Value);
            var r = _regras.FirstOrDefault(x => x.Id == id);
            if (r is null) return;

            if (dgvRegras.Columns[e.ColumnIndex].Name == "colAtiva")
            {
                r.Ativa = (bool)(dgvRegras.Rows[e.RowIndex].Cells["colAtiva"].Value ?? false);
            }
        }

        // ==================== THREADS ====================

        private void CriarSocketRx()
        {
            try
            {
                _udpRecv = new UdpClient(_portaRecepcao);
                _udpRecv.Client.Blocking = true;
                _udpRecv.Client.ReceiveTimeout = 0;
                _rxErroSeq = 0;
            }
            catch
            {
                _udpRecv = null;
            }
        }

        // recebe UDP e joga texto na fila
        private void RxLoop(System.Threading.CancellationToken ct)
        {
            IPEndPoint end = new IPEndPoint(IPAddress.Any, 0);

            while (!ct.IsCancellationRequested && !this.IsDisposed)
            {
                try
                {
                    if (_udpRecv == null)
                    {
                        // E2: tentativa de reconexão com backoff
                        _rxErroSeq = Math.Min(_rxErroSeq + 1, _rxErroSeqMax);
                        int backoff = Math.Min(1000 * _rxErroSeq, 5000);
                        CriarSocketRx();
                        if (_udpRecv == null)
                        {
                            System.Threading.Thread.Sleep(backoff);
                            continue;
                        }
                    }

                    var bytes = _udpRecv.Receive(ref end); 
                    var text = Encoding.UTF8.GetString(bytes);
                    _rxQueue.Enqueue(text);
                    _rxWatch.Restart(); 
                }
                catch (SocketException)
                {
                    // E2/E4: erro; reinicia socket para reconectar
                    try { _udpRecv?.Close(); _udpRecv?.Dispose(); } catch { }
                    _udpRecv = null;
                    System.Threading.Thread.Sleep(50);
                }
                catch
                {
                    System.Threading.Thread.Sleep(1);
                }

                // E4: timeout (inatividade)
                if (_rxWatch.Elapsed > _rxInatividade)
                {
                    try { _udpRecv?.Close(); _udpRecv?.Dispose(); } catch { }
                    _udpRecv = null; // força reconexão
                    _rxWatch.Restart();
                }
            }
        }

        // PROC: consome RX, parseia, atualiza UI e aplica regras/transições
        private void ProcLoop(System.Threading.CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !this.IsDisposed)
            {
                if (!_rxQueue.TryDequeue(out var raw))
                {
                    System.Threading.Thread.Yield(); 
                    continue;
                }

                if (!TryParseMedida(raw, out var m))
                    continue;

                try { this.BeginInvoke(new Action(() => AtualizarGridPacotes(m))); } catch { }

                AvaliarRegrasComTransicoes(m);
            }
        }

        // TX: consome fila e envia com retry curto
        private void TxLoop(System.Threading.CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !this.IsDisposed)
            {
                if (!_txQueue.TryDequeue(out var bytes))
                {
                    System.Threading.Thread.Sleep(1);
                    continue;
                }

                bool ok = false;
                for (int tentativa = 0; tentativa < _txRetryMax && !ok; tentativa++)
                {
                    try
                    {
                        _udpSend ??= new UdpClient();
                        _udpSend.Send(bytes, bytes.Length, Modulo3Host, Modulo3Porta);
                        ok = true;
                        _txErroSeq = 0;
                    }
                    catch
                    {
                        _txErroSeq++;
                        System.Threading.Thread.Sleep(50 * (tentativa + 1)); 
                    }
                }

                if (!ok)
                {
                    // E11: erro de envio (opcional: refletir no txtLog)
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            txtLog.Text = "{\n  \"status\": \"erro de envio (E11)\",\n  \"detalhe\": \"falha após retries\"\n}";
                        }));
                    }
                    catch { }
                }
            }
        }

        // ==================== LÓGICA (UI e Regras) ====================

        private void AtualizarGridPacotes(Medida m)
        {
            dgvPacotes.Rows.Add(m.Timestamp.ToString("HH:mm:ss"), m.IedId, m.IA, m.IB, m.IC);
            if (dgvPacotes.Rows.Count > 200)
                dgvPacotes.Rows.RemoveAt(0);
        }

        // Transições: só conta/gera pacote quando muda (ativo <-> inativo)
        private void AvaliarRegrasComTransicoes(Medida m)
        {
            bool houveMudanca = false;

            foreach (var r in _regras)
            {
                if (!r.Ativa) continue;
                if (r.IedFiltro != "Todos" && r.IedFiltro != m.IedId) continue;

                bool agora = r.Verifica(m);
                var key = (r.Id, m.IedId);
                bool antes = _estadoAnterior.TryGetValue(key, out var prev) && prev;

                if (agora != antes)
                {
                    houveMudanca = true;

                    // contador por IED (apenas em transição)
                    _contadoresPorIed.AddOrUpdate(m.IedId, 1, (_, v) => v + 1);

                    // enfileira pacote p/ TX e atualiza último JSON
                    TryEnviarEventoModulo3(r, m, ativo: agora);
                }

                _estadoAnterior[key] = agora;
            }

            if (houveMudanca)
            {
                System.Threading.Interlocked.Increment(ref _totalEventos);
                try { this.BeginInvoke(new Action(AtualizarContadoresUI)); } catch { }
            }
        }

        private void AtualizarContadoresUI()
        {
            lblTotalEventos.Text = $"Total eventos: {_totalEventos}";
            lvContadores.BeginUpdate();
            lvContadores.Items.Clear();

            foreach (var kv in _contadoresPorIed.OrderBy(k => k.Key))
            {
                var it = new ListViewItem(kv.Key);
                it.SubItems.Add(kv.Value.ToString());
                lvContadores.Items.Add(it);
            }

            lvContadores.EndUpdate();
        }

        // Timer: reflete o último pacote enviado
        private void timerRelatorio_Tick(object? sender, EventArgs e)
        {
            txtLog.Text = _ultimoPacoteJson;
        }

        // MOCK (somente correntes) — DESLIGADO
        private void GerarMedidaMock()
        {
            var m = new Medida(
                DateTime.Now,
                new[] { "IED1", "IED2", "IED3" }[_rng.Next(3)],
                IA: Math.Round(_rng.NextDouble() * 500, 1),
                IB: Math.Round(_rng.NextDouble() * 500, 1),
                IC: Math.Round(_rng.NextDouble() * 500, 1)
            );

            // segue o mesmo caminho do RX real
            AtualizarGridPacotes(m);
            AvaliarRegrasComTransicoes(m);
        }

        // ==================== Helpers ====================

        private static int IedToInt(string ied)
        {
            if (int.TryParse(ied, out var n)) return n;
            var digits = new string(ied.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out n) ? n : 0;
        }

        private static string FormatNumber(double v) =>
            v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        private static string MakeFilterString(Regra r)
        {
            string op = r.Operador switch { Operador.Maior => ">", Operador.Menor => "<", _ => "=" };
            return $"{r.Parametro} {op} {FormatNumber(r.Valor)}";
        }

        private void TryEnviarEventoModulo3(Regra r, Medida m, bool ativo)
        {
            try
            {
                var payload = new
                {
                    id_ied = IedToInt(m.IedId),
                    filter = MakeFilterString(r),
                    @event = ativo
                };

                _ultimoPacoteJson = JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                // enfileira para a thread de TX
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                _txQueue.Enqueue(bytes);
            }
            catch
            {
            }
        }

        private static bool TryParseMedida(string raw, out Medida medida)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string s = raw?.Trim() ?? string.Empty;

            if (s.StartsWith("{"))
            {
                try
                {
                    if (s.Contains("'") && !s.Contains("\"")) s = s.Replace('\'', '"');
                    using var doc = JsonDocument.Parse(s);
                    var root = doc.RootElement;

                    string ied = "IED0";
                    if (root.TryGetProperty("idDispositivo", out var vDisp))
                    {
                        if (vDisp.ValueKind == JsonValueKind.Number && vDisp.TryGetInt32(out int idn))
                            ied = $"IED{idn}";
                        else
                            ied = $"IED{(vDisp.GetString() ?? "0")}";
                    }
                    else if (root.TryGetProperty("id", out var vId))
                    {
                        ied = vId.ValueKind == JsonValueKind.Number ? $"IED{vId.GetInt32()}" : (vId.GetString() ?? "IED0");
                    }
                    else if (root.TryGetProperty("IedId", out var vIed))
                    {
                        ied = vIed.GetString() ?? "IED0";
                    }

                    double ia = root.TryGetProperty("IA", out var vIA) ? vIA.GetDouble()
                              : root.TryGetProperty("Ia", out var vIa) ? vIa.GetDouble() : 0.0;
                    double ib = root.TryGetProperty("IB", out var vIB) ? vIB.GetDouble()
                              : root.TryGetProperty("Ib", out var vIb) ? vIb.GetDouble() : 0.0;
                    double ic = root.TryGetProperty("IC", out var vIC) ? vIC.GetDouble()
                              : root.TryGetProperty("Ic", out var vIc) ? vIc.GetDouble() : 0.0;

                    medida = new Medida(DateTime.Now, ied, ia, ib, ic);
                    return true;
                }
                catch { }
            }

            try
            {
                var p = s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4)
                {
                    medida = new Medida(
                        DateTime.Now,
                        p[0].Trim(),
                        double.Parse(p[1], ci),
                        double.Parse(p[2], ci),
                        double.Parse(p[3], ci)
                    );
                    return true;
                }
            }
            catch { }

            medida = default!;
            return false;
        }
    }
}
