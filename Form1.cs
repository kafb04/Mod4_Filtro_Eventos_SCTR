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
    public enum ComparisonOperator { Less, Equal, Greater }

    public record CurrentMeasurement(DateTime Timestamp, string IedId, double IA, double IB, double IC);

    public class RuleCondition
    {
        public int Id { get; init; }
        public string Parameter { get; set; } = "IA";
        public ComparisonOperator Operator { get; set; } = ComparisonOperator.Greater;
        public double Value { get; set; } = 300.0;
        public bool Active { get; set; } = true;

        public bool Verify(CurrentMeasurement m)
        {
            if (!Active) return false;

            double x = Parameter switch
            {
                "IA" => m.IA,
                "IB" => m.IB,
                "IC" => m.IC,
                _ => double.NaN
            };

            return Operator switch
            {
                ComparisonOperator.Greater => x > Value,
                ComparisonOperator.Equal => Math.Abs(x - Value) < 1e-6,
                ComparisonOperator.Less => x < Value,
                _ => false
            };
        }
    }

    public partial class Form1 : Form
    {
        // ===== Module 3 destination =====
        private const string mod3Ip = "192.168.56.1";
        private const int mod3Port = 6001;
        private static readonly IPEndPoint _mod3Endpoint =
            new IPEndPoint(IPAddress.Parse(mod3Ip), mod3Port);

        // ===== Network rx/tx =====
        private readonly int _receivePort = 5002;
        private UdpClient? _udpReceiver;
        private UdpClient? _udpSender;

        // ===== Rules / state =====
        private readonly List<RuleCondition> _ruleList = new();
        private readonly object _ruleLock = new();
        private int _nextRuleId = 1;
        private readonly ConcurrentDictionary<string, int> _countersByIed = new();
        private int _totalEvents = 0;
        private readonly ConcurrentDictionary<(int rid, string ied), bool> _previousRuleState = new();

        private CancellationTokenSource _cancellationSource = new();

        // ===== Channels =====
        private readonly Channel<string> _receiveChannel =
            Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        private readonly Channel<byte[]> _sendChannel =
            Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2048)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        // ===== Background workers =====
        private Task? _receiverTask;
        private Task? _processorTask;
        private Task? _senderTask;

        // ===== RX robustness =====
        private readonly TimeSpan _receiveInactivity = TimeSpan.FromSeconds(2);
        private readonly Stopwatch _receiveWatch = new();
        private int _receiveErrorSeq = 0;
        private const int _receiveErrorSeqMax = 5;
        private int _sendErrorSeq = 0;
        private const int _sendRetryMax = 3;

        private string _lastJson = "{\n}";
        private readonly Random _rng = new();

        // ===== Level-triggered (continuous filtering) mode =====
        private readonly bool _levelTriggered = true;

        // ===== UI debounce (counters) =====
        private readonly System.Windows.Forms.Timer _countersUpdateTimer = new System.Windows.Forms.Timer();
        private volatile bool _countersDirty = false;

        public Form1()
        {
            InitializeComponent();
            InitializeComboBoxes();

            dgvRegras.CurrentCellDirtyStateChanged += RulesGrid_CurrentCellDirtyStateChanged;

            // Start when the form becomes visible
            this.Shown += (s, e) =>
            {
                timerRelatorio.Interval = 500;
                timerRelatorio.Tick += ReportTimer_Tick;
                timerRelatorio.Start();

                _countersUpdateTimer.Interval = 200;
                _countersUpdateTimer.Tick += (s2, e2) =>
                {
                    if (!_countersDirty) return;
                    _countersDirty = false;
                    UpdateCountersUI();
                };
                _countersUpdateTimer.Start();

                // Send socket (unicast)
                try
                {
                    _udpSender = new UdpClient();
                    _udpSender.Client.SendBufferSize = 1 << 20;
                    // do not enable broadcast
                }
                catch { _udpSender = null; }

                CreateReceiveSocket();
                StartBackgroundWorkers();
                _receiveWatch.Start();
            };

            // Graceful shutdown
            this.FormClosing += async (s, e) =>
            {
                try
                {
                    try { timerRelatorio.Stop(); } catch { }
                    try { _countersUpdateTimer.Stop(); } catch { }

                    _cancellationSource.Cancel();
                    await Task.WhenAll(
                        _receiverTask ?? Task.CompletedTask,
                        _processorTask ?? Task.CompletedTask,
                        _senderTask ?? Task.CompletedTask
                    ).WaitAsync(TimeSpan.FromMilliseconds(300));
                }
                catch { }

                try { _udpReceiver?.Close(); _udpReceiver?.Dispose(); } catch { }
                try { _udpSender?.Close(); _udpSender?.Dispose(); } catch { }
            };
        }

        // ===== UI helpers =====
        private void RunOnUiThread(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch { }
        }

        private void InitializeComboBoxes()
        {
            cbParametro.Items.Clear();
            cbParametro.Items.AddRange(new object[] { "IA", "IB", "IC" });
            cbParametro.SelectedIndex = 0;

            cbOperador.Items.Clear();
            cbOperador.Items.AddRange(new object[] { "<", "=", ">" });
            cbOperador.SelectedIndex = 2;
        }

        private ComparisonOperator ParseOperator(string symbol) =>
            symbol switch { "<" => ComparisonOperator.Less, "=" => ComparisonOperator.Equal, ">" => ComparisonOperator.Greater, _ => ComparisonOperator.Greater };

        private void btnAdicionarRegra_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(
                    txtValor.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val))
            {
                MessageBox.Show("Invalid value.");
                return;
            }

            var r = new RuleCondition
            {
                Id = _nextRuleId++,
                Parameter = cbParametro.SelectedItem?.ToString() ?? "IA",
                Operator = ParseOperator(cbOperador.SelectedItem?.ToString() ?? ">"),
                Value = val,
                Active = true
            };

            lock (_ruleLock) { _ruleList.Add(r); }
            AddRuleToGrid(r);
        }

        private void AddRuleToGrid(RuleCondition r)
        {
            int row = dgvRegras.Rows.Add();
            var rr = dgvRegras.Rows[row];
            rr.Cells["colId"].Value = r.Id;
            rr.Cells["colParametro"].Value = r.Parameter;
            rr.Cells["colOperador"].Value = r.Operator switch { ComparisonOperator.Greater => ">", ComparisonOperator.Equal => "=", _ => "<" };
            rr.Cells["colValor"].Value = r.Value;
            rr.Cells["colAtiva"].Value = r.Active;
        }

        private void RulesGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
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
                RuleCondition? r;
                lock (_ruleLock)
                {
                    r = _ruleList.FirstOrDefault(x => x.Id == id);
                    if (r != null) _ruleList.Remove(r);
                }

                if (r != null) ForceDeactivateRule(r);
                dgvRegras.Rows.RemoveAt(e.RowIndex);
            }
        }

        private void dgvRegras_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgvRegras.Columns[e.ColumnIndex].Name != "colAtiva") return;

            int id = Convert.ToInt32(dgvRegras.Rows[e.RowIndex].Cells["colId"].Value);
            RuleCondition? r; lock (_ruleLock) { r = _ruleList.FirstOrDefault(x => x.Id == id); }
            if (r is null) return;

            bool active = (bool)(dgvRegras.Rows[e.RowIndex].Cells["colAtiva"].Value ?? false);
            r.Active = active;

            if (!active) ForceDeactivateRule(r);
        }

        // ===== Sockets =====
        private void CreateReceiveSocket()
        {
            try
            {
                _udpReceiver = new UdpClient(AddressFamily.InterNetwork);

                // stable rebind
                _udpReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                _udpReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                _udpReceiver.EnableBroadcast = true;
                _udpReceiver.Client.ReceiveBufferSize = 1 << 20;
                _udpReceiver.Client.SendBufferSize = 1 << 20;

                _udpReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, _receivePort));
                _udpReceiver.Client.Blocking = true;

                _receiveErrorSeq = 0;
            }
            catch
            {
                _udpReceiver = null;
            }
        }

        private void StartBackgroundWorkers()
        {
            var ct = _cancellationSource.Token;

            _receiverTask = Task.Run(() => RunReceiveLoopAsync(ct), ct);
            _processorTask = Task.Run(() => RunProcessingLoopAsync(_receiveChannel.Reader, ct), ct);
            _senderTask = Task.Run(() => RunSendLoopAsync(ct), ct);
        }

        private async Task RunReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                try
                {
                    if (_udpReceiver is null)
                    {
                        _receiveErrorSeq = Math.Min(_receiveErrorSeq + 1, _receiveErrorSeqMax);
                        int backoff = Math.Min(200 * _receiveErrorSeq, 2000);
                        CreateReceiveSocket();
                        if (_udpReceiver is null)
                        {
                            await Task.Delay(backoff, ct);
                            continue;
                        }
                    }

                    var sock = _udpReceiver;
                    if (sock is null) continue;

                    var receiveTask = sock.ReceiveAsync();
                    var timeoutTask = Task.Delay(_receiveInactivity, ct);
                    var winner = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

                    if (winner == timeoutTask)
                    {
                        try { _udpReceiver?.Close(); _udpReceiver?.Dispose(); } catch { }
                        _udpReceiver = null;
                        await Task.Delay(100, ct);
                        continue;
                    }

                    var result = await receiveTask.ConfigureAwait(false);
                    string text = Encoding.UTF8.GetString(result.Buffer);
                    await _receiveChannel.Writer.WriteAsync(text, ct).ConfigureAwait(false);
                    _receiveWatch.Restart();
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException)
                {
                    _udpReceiver = null;
                    await Task.Delay(50, ct);
                }
                catch (SocketException)
                {
                    try { _udpReceiver?.Close(); _udpReceiver?.Dispose(); } catch { }
                    _udpReceiver = null;
                    await Task.Delay(100, ct);
                }
                catch
                {
                    await Task.Delay(10, ct);
                }
            }
        }

        private async Task RunProcessingLoopAsync(ChannelReader<string> rxReader, CancellationToken ct)
        {
            var tick = TimeSpan.FromMilliseconds(50);
            var buffer = new List<CurrentMeasurement>(64);

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
                    FlushUiBatch(buffer); // no-op (grid removed from UI)
                }
            }
        }

        private async Task RunSendLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                byte[] bytes;
                try
                {
                    bytes = await _sendChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }

                bool ok = false;
                for (int attempt = 0; attempt < _sendRetryMax && !ok; attempt++)
                {
                    try
                    {
                        _udpSender ??= new UdpClient();
                        var dest = _mod3Endpoint; // snapshot
                        await _udpSender.SendAsync(bytes, bytes.Length, dest);
                        ok = true;
                        _sendErrorSeq = 0;
                    }
                    catch
                    {
                        _sendErrorSeq++;
                        await Task.Delay(50 * (attempt + 1), ct);
                    }
                }

                if (!ok)
                {
                    RunOnUiThread(() =>
                    {
                        txtLog.Text = "{\n  \"status\": \"send error (E11)\",\n  \"detail\": \"failed after retries\"\n}";
                    });
                }
            }
        }

        // ======= no-op: we removed the per-packet grid from UI =======
        private void FlushUiBatch(List<CurrentMeasurement> buffer) { }

        private void UpdateCountersUI()
        {
            RunOnUiThread(() =>
            {
                lblTotalEventos.Text = $"Total events: {_totalEvents}";
                lvContadores.BeginUpdate();
                lvContadores.Items.Clear();

                foreach (var kv in _countersByIed.OrderBy(k => k.Key))
                {
                    var it = new ListViewItem(kv.Key);
                    it.SubItems.Add(kv.Value.ToString());
                    lvContadores.Items.Add(it);
                }

                lvContadores.EndUpdate();
            });
        }

        private void ReportTimer_Tick(object? sender, EventArgs e)
        {
            txtLog.Text = _lastJson;
        }

        private void EvaluateRulesWithTransitions(CurrentMeasurement m)
        {
            bool changed = false;

            RuleCondition[] rulesSnapshot;
            lock (_ruleLock) { rulesSnapshot = _ruleList.ToArray(); }

            foreach (var r in rulesSnapshot)
            {
                if (!r.Active) continue;

                bool now = r.Verify(m);
                var key = (r.Id, m.IedId);
                bool before = _previousRuleState.TryGetValue(key, out var prev) && prev;

                if (_levelTriggered)
                {
                    if (now)
                    {
                        TrySendEventToMod3(r, m, true);
                        _countersByIed.AddOrUpdate(m.IedId, 1, (_, v) => v + 1);
                        changed = true;
                    }
                    _previousRuleState[key] = now;
                }
                else
                {
                    if (now != before)
                    {
                        TrySendEventToMod3(r, m, now);
                        _countersByIed.AddOrUpdate(m.IedId, 1, (_, v) => v + 1);
                        changed = true;
                    }
                    _previousRuleState[key] = now;
                }
            }

            if (changed)
            {
                Interlocked.Increment(ref _totalEvents);
                _countersDirty = true; // UI updates on timer
            }
        }

        // ======= send with fallback + UI log on error =======
        private void TrySendEventToMod3(RuleCondition r, CurrentMeasurement m, bool active)
        {
            int idied = ConvertIedToInt(m.IedId);
            string filter = BuildFilterString(r);

            try
            {
                _lastJson = JsonMod3.BuildString(idied, filter, active);
                var bytes = JsonMod3.BuildBytes(idied, filter, active);
                _sendChannel.Writer.TryWrite(bytes);
            }
            catch (Exception ex)
            {
                _lastJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    outputData = new { id_ied = idied, filter, active },
                    err = "fallback",
                    detail = ex.Message
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                RunOnUiThread(() => { try { txtLog.Text = _lastJson; } catch { } });

                var fallback = Encoding.UTF8.GetBytes(_lastJson);
                _sendChannel.Writer.TryWrite(fallback);
            }
        }

        private void ForceDeactivateRule(RuleCondition r)
        {
            var toTurnOff = _previousRuleState
                .Where(kv => kv.Key.rid == r.Id && kv.Value)
                .Select(kv => kv.Key.ied)
                .Distinct()
                .ToList();

            foreach (var ied in toTurnOff)
            {
                _previousRuleState[(r.Id, ied)] = false;
                _lastJson = JsonMod3.BuildString(ConvertIedToInt(ied), BuildFilterString(r), false);
                _sendChannel.Writer.TryWrite(JsonMod3.BuildBytes(ConvertIedToInt(ied), BuildFilterString(r), false));
                _countersByIed.AddOrUpdate(ied, 1, (_, v) => v + 1);
            }

            _countersDirty = true;
        }

        private static int ConvertIedToInt(string ied)
        {
            if (int.TryParse(ied, out var n)) return n;
            var digits = new string(ied.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out n) ? n : 0;
        }

        private static string FormatNumber(double v) =>
            v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        private static string BuildFilterString(RuleCondition r)
        {
            string op = r.Operator switch { ComparisonOperator.Greater => ">", ComparisonOperator.Less => "<", _ => "=" };
            return $"{r.Parameter} {op} {FormatNumber(r.Value)}";
        }

        private static bool TryParseMeasurement(string raw, out CurrentMeasurement measurement)
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

                    measurement = new CurrentMeasurement(DateTime.Now, ied, ia, ib, ic);
                    return true;
                }
                catch { }
            }

            try
            {
                var p = s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4
                    && double.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var pa)
                    && double.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out var pb)
                    && double.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out var pc))
                {
                    measurement = new CurrentMeasurement(DateTime.Now, p[0].Trim(), pa, pb, pc);
                    return true;
                }
            }
            catch { }

            measurement = default!;
            return false;
        }
    }
}
