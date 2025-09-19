using Modulo4FilterEventsWinForms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Modulo4FiltroEventosWinForms
{
    public enum Operator { Less, Equal, Greater }

    public record Measurement(DateTime Timestamp, string IedId, double IA, double IB, double IC);

    public class Rule
    {
        public int Id { get; init; }
        public string Parameter { get; set; } = "IA";
        public Operator Operator { get; set; } = Operator.Greater;
        public double Value { get; set; } = 300.0;
        public string IedFilter { get; set; } = "Todos";
        public bool Active { get; set; } = true;

        public bool Verify(Measurement m)
        {
            if (!Active) return false;
            if (IedFilter != "Todos" && m.IedId != IedFilter) return false;

            double x = Parameter switch
            {
                "IA" => m.IA,
                "IB" => m.IB,
                "IC" => m.IC,
                _ => double.NaN
            };

            return Operator switch
            {
                Operator.Greater => x > Value,
                Operator.Equal => Math.Abs(x - Value) < 1e-6,
                Operator.Less => x < Value,
                _ => false
            };
        }
    }

    public partial class Form1 : Form
    {
        private readonly int _receivePort = 5002;
        private UdpClient? _udpRecv;

        private const string Mod3Host = "127.0.0.1";
        private const int Mod3Port = 6001;
        private UdpClient? _udpSend;

        private readonly List<Rule> _rules = new();
        private int _nextRuleId = 1;
        private readonly ConcurrentDictionary<string, int> _countersByIed = new();
        private int _totalEvents = 0;
        private readonly ConcurrentDictionary<(int rid, string ied), bool> _prevState = new();

        private CancellationTokenSource _cts = new();

        private readonly Channel<string> _rxChannel =
            Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        private readonly Channel<byte[]> _txChannel =
            Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2048)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        private Task? _rxTask;
        private Task? _procTask;
        private Task? _txTask;

        private readonly TimeSpan _rxInactivity = TimeSpan.FromSeconds(2);
        private readonly Stopwatch _rxWatch = new();
        private int _rxErrorSeq = 0;
        private const int _rxErrorSeqMax = 5;
        private int _txErrorSeq = 0;
        private const int _txRetryMax = 3;

        private string _lastJson = "{\n}";
        private readonly Random _rng = new();
        private const bool USE_MOCK = false;

        public Form1()
        {
            InitializeComponent();
            InitCombos();

            dgvRegras.CurrentCellDirtyStateChanged += dgvRegras_CurrentCellDirtyStateChanged;

            timerRelatorio.Interval = 500;
            timerRelatorio.Tick += timerRelatorio_Tick;
            timerRelatorio.Start();

            timerMock.Interval = 300;
            if (USE_MOCK)
                timerMock.Tick += (_, __) => GenerateMockMeasurement();

            try
            {
                _udpSend = new UdpClient();
                _udpSend.Client.SendBufferSize = 1 << 20;
            }
            catch { _udpSend = null; }

            CreateSocketRx();
            StartWorkers();
            _rxWatch.Start();

            this.FormClosing += async (s, e) =>
            {
                try
                {
                    _cts.Cancel();
                    await Task.WhenAll(
                        _rxTask ?? Task.CompletedTask,
                        _procTask ?? Task.CompletedTask,
                        _txTask ?? Task.CompletedTask
                    ).WaitAsync(TimeSpan.FromMilliseconds(300));
                }
                catch { }

                try { _udpRecv?.Close(); _udpRecv?.Dispose(); } catch { }
                try { _udpSend?.Close(); _udpSend?.Dispose(); } catch { }
            };
        }

        private void InitCombos()
        {
            cbParametro.Items.Clear();
            cbParametro.Items.AddRange(new object[] { "IA", "IB", "IC" });
            cbParametro.SelectedIndex = 0;

            cbOperador.Items.Clear();
            cbOperador.Items.AddRange(new object[] { "<", "=", ">" });
            cbOperador.SelectedIndex = 2;

            nudIed.Minimum = 1;
            nudIed.Maximum = 100000;
            nudIed.Value = 1;
            nudIed.Enabled = !chkTodosIeds.Checked;
            chkTodosIeds.CheckedChanged += (s, e) => nudIed.Enabled = !chkTodosIeds.Checked;
        }

        private Operator ParseOperator(string symbol) =>
            symbol switch { "<" => Operator.Less, "=" => Operator.Equal, ">" => Operator.Greater, _ => Operator.Greater };

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

            var r = new Rule
            {
                Id = _nextRuleId++,
                Parameter = cbParametro.SelectedItem?.ToString() ?? "IA",
                Operator = ParseOperator(cbOperador.SelectedItem?.ToString() ?? ">"),
                Value = val,
                IedFilter = chkTodosIeds.Checked ? "Todos" : $"IED{nudIed.Value}",
                Active = true
            };

            _rules.Add(r);
            AddRuleToGrid(r);
        }

        private void AddRuleToGrid(Rule r)
        {
            int row = dgvRegras.Rows.Add();
            var rr = dgvRegras.Rows[row];
            rr.Cells["colId"].Value = r.Id;
            rr.Cells["colParametro"].Value = r.Parameter;
            rr.Cells["colOperador"].Value = r.Operator switch { Operator.Greater => ">", Operator.Equal => "=", _ => "<" };
            rr.Cells["colValor"].Value = r.Value;
            rr.Cells["colIed"].Value = r.IedFilter;
            rr.Cells["colAtiva"].Value = r.Active;
        }

        private void dgvRegras_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (dgvRegras.IsCurrentCellDirty)
                dgvRegras.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void dgvRegras_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgvRegras.Columns[e.ColumnIndex].Name == "colRemover")
            {
                int id = Convert.ToInt32(dgvRegras.Rows[e.RowIndex].Cells["colId"].Value);
                var r = _rules.FirstOrDefault(x => x.Id == id);
                if (r != null)
                {
                    ForceOffForRule(r); 
                    _rules.Remove(r);
                }

                dgvRegras.Rows.RemoveAt(e.RowIndex);
            }
        }

        private void dgvRegras_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgvRegras.Columns[e.ColumnIndex].Name != "colAtiva") return;

            int id = Convert.ToInt32(dgvRegras.Rows[e.RowIndex].Cells["colId"].Value);
            var r = _rules.FirstOrDefault(x => x.Id == id);
            if (r is null) return;

            bool ativa = (bool)(dgvRegras.Rows[e.RowIndex].Cells["colAtiva"].Value ?? false);
            r.Active = ativa;

            if (!ativa)
                ForceOffForRule(r);
        }

        private void CreateSocketRx()
        {
            try
            {
                _udpRecv = new UdpClient(AddressFamily.InterNetwork);
                _udpRecv.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpRecv.EnableBroadcast = true;
                _udpRecv.Client.ReceiveBufferSize = 1 << 20;
                _udpRecv.Client.SendBufferSize = 1 << 20;
                _udpRecv.Client.Bind(new IPEndPoint(IPAddress.Any, _receivePort));
                _udpRecv.Client.Blocking = true;
                _udpRecv.Client.ReceiveTimeout = 0;
                _rxErrorSeq = 0;
            }
            catch { _udpRecv = null; }
        }

        private void StartWorkers()
        {
            var ct = _cts.Token;
            _rxTask = Task.Run(() => RunRxAsync(ct), ct);
            _procTask = Task.Run(() => RunFsmHybridAsync(_rxChannel.Reader, ct), ct);
            _txTask = Task.Run(() => RunTxAsync(ct), ct);
        }

        private async Task RunRxAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                try
                {
                    if (_udpRecv is null)
                    {
                        _rxErrorSeq = Math.Min(_rxErrorSeq + 1, _rxErrorSeqMax);
                        int backoff = Math.Min(1000 * _rxErrorSeq, 5000);
                        CreateSocketRx();
                        if (_udpRecv is null)
                        {
                            await Task.Delay(backoff, ct);
                            continue;
                        }
                    }

                    var result = await _udpRecv.ReceiveAsync().ConfigureAwait(false);
                    string text = Encoding.UTF8.GetString(result.Buffer);
                    await _rxChannel.Writer.WriteAsync(text, ct).ConfigureAwait(false);
                    _rxWatch.Restart();
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException)
                {
                    try { _udpRecv?.Close(); _udpRecv?.Dispose(); } catch { }
                    _udpRecv = null;
                    await Task.Delay(50, ct);
                }
                catch
                {
                    await Task.Delay(1, ct);
                }

                if (_rxWatch.Elapsed > _rxInactivity)
                {
                    try { _udpRecv?.Close(); _udpRecv?.Dispose(); } catch { }
                    _udpRecv = null;
                    _rxWatch.Restart();
                }
            }
        }

        private async Task RunFsmHybridAsync(ChannelReader<string> rxReader, CancellationToken ct)
        {
            var tick = TimeSpan.FromMilliseconds(50);
            var buffer = new List<Measurement>(64);

            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                var readTask = rxReader.ReadAsync(ct).AsTask();
                var delayTask = Task.Delay(tick, ct);
                var winner = await Task.WhenAny(readTask, delayTask);

                buffer.Clear();

                if (winner == readTask)
                {
                    var raw = await readTask;
                    if (TryParseMeasurement(raw, out var m1)) buffer.Add(m1);
                    while (rxReader.TryRead(out var raw2))
                        if (TryParseMeasurement(raw2, out var m2)) buffer.Add(m2);
                }

                if (buffer.Count > 0)
                {
                    foreach (var mm in buffer) EvaluateRulesWithTransitions(mm);
                    FlushUIBatch(buffer);
                }
            }
        }

        private async Task RunTxAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                byte[] bytes;
                try
                {
                    bytes = await _txChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }

                bool ok = false;
                for (int attempt = 0; attempt < _txRetryMax && !ok; attempt++)
                {
                    try
                    {
                        _udpSend ??= new UdpClient();
                        await _udpSend.SendAsync(bytes, bytes.Length, Mod3Host, Mod3Port);
                        ok = true;
                        _txErrorSeq = 0;
                    }
                    catch
                    {
                        _txErrorSeq++;
                        await Task.Delay(50 * (attempt + 1), ct);
                    }
                }

                if (!ok)
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            txtLog.Text = "{\n  \"status\": \"erro de envio (E11)\",\n  \"detalhe\": \"falha após tentativas\"\n}";
                        }));
                    }
                    catch { }
                }
            }
        }

        private void FlushUIBatch(List<Measurement> buffer)
        {
            if (buffer.Count == 0) return;
            var copy = buffer.ToArray();

            try
            {
                BeginInvoke(new Action(() =>
                {
                    foreach (var m in copy)
                    {
                        dgvPacotes.Rows.Add(m.Timestamp.ToString("HH:mm:ss"), m.IedId, m.IA, m.IB, m.IC);
                        if (dgvPacotes.Rows.Count > 200)
                            dgvPacotes.Rows.RemoveAt(0);
                    }
                }));
            }
            catch { }
        }

        private void UpdateCountersUI()
        {
            lblTotalEventos.Text = $"Total eventos: {_totalEvents}";
            lvContadores.BeginUpdate();
            lvContadores.Items.Clear();

            foreach (var kv in _countersByIed.OrderBy(k => k.Key))
            {
                var it = new ListViewItem(kv.Key);
                it.SubItems.Add(kv.Value.ToString());
                lvContadores.Items.Add(it);
            }

            lvContadores.EndUpdate();
        }

        private void timerRelatorio_Tick(object? sender, EventArgs e)
        {
            txtLog.Text = _lastJson;
        }

        private void EvaluateRulesWithTransitions(Measurement m)
        {
            bool changed = false;

            foreach (var r in _rules)
            {
                if (!r.Active) continue;
                if (r.IedFilter != "Todos" && r.IedFilter != m.IedId) continue;

                bool now = r.Verify(m);
                var key = (r.Id, m.IedId);
                bool before = _prevState.TryGetValue(key, out var prev) && prev;

                if (now != before)
                {
                    changed = true;
                    _countersByIed.AddOrUpdate(m.IedId, 1, (_, v) => v + 1);
                    TrySendEventToMod3(r, m, now);
                }

                _prevState[key] = now;
            }

            if (changed)
            {
                Interlocked.Increment(ref _totalEvents);
                try { BeginInvoke(new Action(UpdateCountersUI)); } catch { }
            }
        }

        private void TrySendEventToMod3(Rule r, Measurement m, bool active)
        {
            try
            {
                int idIed = IedToInt(m.IedId);
                string filter = MakeFilterString(r);

                _lastJson = JsonMod3.BuildString(idIed, filter, active);
                _txChannel.Writer.TryWrite(JsonMod3.BuildBytes(idIed, filter, active));
            }
            catch { }
        }

        private void ForceOffForRule(Rule r)
        {
            var toTurnOff = _prevState
                .Where(kv => kv.Key.rid == r.Id && kv.Value)
                .Select(kv => kv.Key.ied)
                .Distinct()
                .ToList();

            foreach (var ied in toTurnOff)
            {
                _prevState[(r.Id, ied)] = false;
                _lastJson = JsonMod3.BuildString(IedToInt(ied), MakeFilterString(r), false);
                _txChannel.Writer.TryWrite(JsonMod3.BuildBytes(IedToInt(ied), MakeFilterString(r), false));
                _countersByIed.AddOrUpdate(ied, 1, (_, v) => v + 1);
            }

            try { BeginInvoke(new Action(UpdateCountersUI)); } catch { }
        }

        private void GenerateMockMeasurement()
        {
            var m = new Measurement(
                DateTime.Now,
                new[] { "IED1", "IED2", "IED3" }[_rng.Next(3)],
                IA: Math.Round(_rng.NextDouble() * 500, 1),
                IB: Math.Round(_rng.NextDouble() * 500, 1),
                IC: Math.Round(_rng.NextDouble() * 500, 1)
            );

            EvaluateRulesWithTransitions(m);
            FlushUIBatch(new List<Measurement> { m });
        }

        private static int IedToInt(string ied)
        {
            if (int.TryParse(ied, out var n)) return n;
            var digits = new string(ied.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out n) ? n : 0;
        }

        private static string FormatNumber(double v) =>
            v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        private static string MakeFilterString(Rule r)
        {
            string op = r.Operator switch { Operator.Greater => ">", Operator.Less => "<", _ => "=" };
            return $"{r.Parameter} {op} {FormatNumber(r.Value)}";
        }

        private static bool TryParseMeasurement(string raw, out Measurement measurement)
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

                    measurement = new Measurement(DateTime.Now, ied, ia, ib, ic);
                    return true;
                }
                catch { }
            }

            try
            {
                var p = s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4)
                {
                    measurement = new Measurement(
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

            measurement = default!;
            return false;
        }
    }
}
