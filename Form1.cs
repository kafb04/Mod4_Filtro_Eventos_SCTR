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
        public bool Active { get; set; } = true;

        public bool Verify(Measurement m)
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
                Operator.Greater => x > Value,
                Operator.Equal => Math.Abs(x - Value) < 1e-6,
                Operator.Less => x < Value,
                _ => false
            };
        }
    }

    public partial class Form1 : Form
    {
        // ===== destino do módulo 3 =====
        private const string mod3_ip = "192.168.56.1";
        private const int mod3_port = 6001;
        private static readonly IPEndPoint _mod3endpoint =
            new IPEndPoint(IPAddress.Parse(mod3_ip), mod3_port);

        // ===== rede rx/tx =====
        private readonly int _receiveport = 5002;
        private UdpClient? _udprecv;
        private UdpClient? _udpsend;

        // ===== regras/estado =====
        private readonly List<Rule> _rules = new();
        private readonly object _ruleslock = new();
        private int _nextruleid = 1;
        private readonly ConcurrentDictionary<string, int> _countersbyied = new();
        private int _totalevents = 0;
        private readonly ConcurrentDictionary<(int rid, string ied), bool> _prevstate = new();

        private CancellationTokenSource _cts = new();

        // ===== canais =====
        private readonly Channel<string> _rxchannel =
            Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        private readonly Channel<byte[]> _txchannel =
            Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2048)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        // ===== workers =====
        private Task? _rxtask;
        private Task? _proctask;
        private Task? _txtask;

        // ===== robustez rx =====
        private readonly TimeSpan _rxinactivity = TimeSpan.FromSeconds(2);
        private readonly Stopwatch _rxwatch = new();
        private int _rxerrorseq = 0;
        private const int _rxerrorseqmax = 5;
        private int _txerrorseq = 0;
        private const int _txretrymax = 3;

        private string _lastjson = "{\n}";
        private readonly Random _rng = new();

        // ===== modo nível (filtragem contínua) =====
        private readonly bool _leveltriggered = true;

        // ===== debounce da ui (contadores) =====
        private readonly System.Windows.Forms.Timer _timercounters = new System.Windows.Forms.Timer();
        private volatile bool _countersdirty = false;

        public Form1()
        {
            InitializeComponent();
            initcombos();

            dgvRegras.CurrentCellDirtyStateChanged += dgvregras_currentcelldirtystatechanged;

            // inicia quando o form estiver visível
            this.Shown += (s, e) =>
            {
                timerRelatorio.Interval = 500;
                timerRelatorio.Tick += timerrelatorio_tick;
                timerRelatorio.Start();

                _timercounters.Interval = 200;
                _timercounters.Tick += (s2, e2) =>
                {
                    if (!_countersdirty) return;
                    _countersdirty = false;
                    updatecountersui();
                };
                _timercounters.Start();

                // socket de envio (unicast)
                try
                {
                    _udpsend = new UdpClient();
                    _udpsend.Client.SendBufferSize = 1 << 20;
                    // não habilitar broadcast
                }
                catch { _udpsend = null; }

                createsocketrx();
                startworkers();
                _rxwatch.Start();
            };

            // fechamento limpo
            this.FormClosing += async (s, e) =>
            {
                try
                {
                    try { timerRelatorio.Stop(); } catch { }
                    try { _timercounters.Stop(); } catch { }

                    _cts.Cancel();
                    await Task.WhenAll(
                        _rxtask ?? Task.CompletedTask,
                        _proctask ?? Task.CompletedTask,
                        _txtask ?? Task.CompletedTask
                    ).WaitAsync(TimeSpan.FromMilliseconds(300));
                }
                catch { }

                try { _udprecv?.Close(); _udprecv?.Dispose(); } catch { }
                try { _udpsend?.Close(); _udpsend?.Dispose(); } catch { }
            };
        }

        // ===== helpers de ui =====
        private void ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch { }
        }

        private void initcombos()
        {
            cbParametro.Items.Clear();
            cbParametro.Items.AddRange(new object[] { "IA", "IB", "IC" });
            cbParametro.SelectedIndex = 0;

            cbOperador.Items.Clear();
            cbOperador.Items.AddRange(new object[] { "<", "=", ">" });
            cbOperador.SelectedIndex = 2;
        }

        private Operator parseoperator(string symbol) =>
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
                Id = _nextruleid++,
                Parameter = cbParametro.SelectedItem?.ToString() ?? "IA",
                Operator = parseoperator(cbOperador.SelectedItem?.ToString() ?? ">"),
                Value = val,
                Active = true
            };

            lock (_ruleslock) { _rules.Add(r); }
            addrultogrid(r);
        }

        private void addrultogrid(Rule r)
        {
            int row = dgvRegras.Rows.Add();
            var rr = dgvRegras.Rows[row];
            rr.Cells["colId"].Value = r.Id;
            rr.Cells["colParametro"].Value = r.Parameter;
            rr.Cells["colOperador"].Value = r.Operator switch { Operator.Greater => ">", Operator.Equal => "=", _ => "<" };
            rr.Cells["colValor"].Value = r.Value;
            rr.Cells["colAtiva"].Value = r.Active;
        }

        private void dgvregras_currentcelldirtystatechanged(object? sender, EventArgs e)
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
                Rule? r;
                lock (_ruleslock)
                {
                    r = _rules.FirstOrDefault(x => x.Id == id);
                    if (r != null) _rules.Remove(r);
                }

                if (r != null) forceoffforrule(r);
                dgvRegras.Rows.RemoveAt(e.RowIndex);
            }
        }

        private void dgvRegras_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgvRegras.Columns[e.ColumnIndex].Name != "colAtiva") return;

            int id = Convert.ToInt32(dgvRegras.Rows[e.RowIndex].Cells["colId"].Value);
            Rule? r; lock (_ruleslock) { r = _rules.FirstOrDefault(x => x.Id == id); }
            if (r is null) return;

            bool ativa = (bool)(dgvRegras.Rows[e.RowIndex].Cells["colAtiva"].Value ?? false);
            r.Active = ativa;

            if (!ativa) forceoffforrule(r);
        }

        // ===== sockets =====
        private void createsocketrx()
        {
            try
            {
                _udprecv = new UdpClient(AddressFamily.InterNetwork);

                // rebind estável
                _udprecv.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                _udprecv.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                _udprecv.EnableBroadcast = true;
                _udprecv.Client.ReceiveBufferSize = 1 << 20;
                _udprecv.Client.SendBufferSize = 1 << 20;

                _udprecv.Client.Bind(new IPEndPoint(IPAddress.Any, _receiveport));
                _udprecv.Client.Blocking = true;

                _rxerrorseq = 0;
            }
            catch
            {
                _udprecv = null;
            }
        }

        private void startworkers()
        {
            var ct = _cts.Token;

            _rxtask = Task.Run(() => runrxasync(ct), ct);
            _proctask = Task.Run(() => runfsmhybridasync(_rxchannel.Reader, ct), ct);
            _txtask = Task.Run(() => runtxasync(ct), ct);
        }

        private async Task runrxasync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                try
                {
                    if (_udprecv is null)
                    {
                        _rxerrorseq = Math.Min(_rxerrorseq + 1, _rxerrorseqmax);
                        int backoff = Math.Min(200 * _rxerrorseq, 2000);
                        createsocketrx();
                        if (_udprecv is null)
                        {
                            await Task.Delay(backoff, ct);
                            continue;
                        }
                    }

                    var sock = _udprecv;
                    if (sock is null) continue;

                    var recvtask = sock.ReceiveAsync();
                    var timeouttask = Task.Delay(_rxinactivity, ct);
                    var winner = await Task.WhenAny(recvtask, timeouttask).ConfigureAwait(false);

                    if (winner == timeouttask)
                    {
                        try { _udprecv?.Close(); _udprecv?.Dispose(); } catch { }
                        _udprecv = null;
                        await Task.Delay(100, ct);
                        continue;
                    }

                    var result = await recvtask.ConfigureAwait(false);
                    string text = Encoding.UTF8.GetString(result.Buffer);
                    await _rxchannel.Writer.WriteAsync(text, ct).ConfigureAwait(false);
                    _rxwatch.Restart();
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException)
                {
                    _udprecv = null;
                    await Task.Delay(50, ct);
                }
                catch (SocketException)
                {
                    try { _udprecv?.Close(); _udprecv?.Dispose(); } catch { }
                    _udprecv = null;
                    await Task.Delay(100, ct);
                }
                catch
                {
                    await Task.Delay(10, ct);
                }
            }
        }

        private async Task runfsmhybridasync(ChannelReader<string> rxreader, CancellationToken ct)
        {
            var tick = TimeSpan.FromMilliseconds(50);
            var buffer = new List<Measurement>(64);

            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                var readtask = rxreader.ReadAsync(ct).AsTask();
                var delaytask = Task.Delay(tick, ct);
                var winner = await Task.WhenAny(readtask, delaytask);

                buffer.Clear();

                if (winner == readtask)
                {
                    var raw = await readtask;
                    if (tryparsemeasurement(raw, out var m1)) buffer.Add(m1);
                    while (rxreader.TryRead(out var raw2))
                        if (tryparsemeasurement(raw2, out var m2)) buffer.Add(m2);
                }

                if (buffer.Count > 0)
                {
                    foreach (var mm in buffer) evaluateruleswithtransitions(mm);
                    flushuibatch(buffer); // <- corrigido
                }
            }
        }

        private async Task runtxasync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsDisposed)
            {
                byte[] bytes;
                try
                {
                    bytes = await _txchannel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }

                bool ok = false;
                for (int attempt = 0; attempt < _txretrymax && !ok; attempt++)
                {
                    try
                    {
                        _udpsend ??= new UdpClient();
                        var dest = _mod3endpoint; // snapshot
                        await _udpsend.SendAsync(bytes, bytes.Length, dest);
                        ok = true;
                        _txerrorseq = 0;
                    }
                    catch
                    {
                        _txerrorseq++;
                        await Task.Delay(50 * (attempt + 1), ct);
                    }
                }

                if (!ok)
                {
                    ui(() =>
                    {
                        txtLog.Text = "{\n  \"status\": \"erro de envio (E11)\",\n  \"detalhe\": \"falha após tentativas\"\n}";
                    });
                }
            }
        }

        // ======= no-op: removemos a grade de pacotes da ui =======
        private void flushuibatch(List<Measurement> buffer) { }

        private void updatecountersui()
        {
            ui(() =>
            {
                lblTotalEventos.Text = $"Total eventos: {_totalevents}";
                lvContadores.BeginUpdate();
                lvContadores.Items.Clear();

                foreach (var kv in _countersbyied.OrderBy(k => k.Key))
                {
                    var it = new ListViewItem(kv.Key);
                    it.SubItems.Add(kv.Value.ToString());
                    lvContadores.Items.Add(it);
                }

                lvContadores.EndUpdate();
            });
        }

        private void timerrelatorio_tick(object? sender, EventArgs e)
        {
            txtLog.Text = _lastjson;
        }

        private void evaluateruleswithtransitions(Measurement m)
        {
            bool changed = false;

            Rule[] rulessnapshot;
            lock (_ruleslock) { rulessnapshot = _rules.ToArray(); }

            foreach (var r in rulessnapshot)
            {
                if (!r.Active) continue;

                bool now = r.Verify(m);
                var key = (r.Id, m.IedId);
                bool before = _prevstate.TryGetValue(key, out var prev) && prev;

                if (_leveltriggered)
                {
                    if (now)
                    {
                        trysendeventtomod3(r, m, true);
                        _countersbyied.AddOrUpdate(m.IedId, 1, (_, v) => v + 1);
                        changed = true;
                    }
                    _prevstate[key] = now;
                }
                else
                {
                    if (now != before)
                    {
                        trysendeventtomod3(r, m, now);
                        _countersbyied.AddOrUpdate(m.IedId, 1, (_, v) => v + 1);
                        changed = true;
                    }
                    _prevstate[key] = now;
                }
            }

            if (changed)
            {
                Interlocked.Increment(ref _totalevents);
                _countersdirty = true; // debounce: ui atualiza no timer
            }
        }

        // ======= envio com fallback + log em caso de erro =======
        private void trysendeventtomod3(Rule r, Measurement m, bool active)
        {
            int idied = iedtoint(m.IedId);
            string filter = makefilterstring(r);

            try
            {
                _lastjson = JsonMod3.BuildString(idied, filter, active);
                var bytes = JsonMod3.BuildBytes(idied, filter, active);
                _txchannel.Writer.TryWrite(bytes);
            }
            catch (Exception ex)
            {
                _lastjson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    outputData = new { id_ied = idied, filter, active },
                    err = "fallback",
                    detail = ex.Message
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                ui(() => { try { txtLog.Text = _lastjson; } catch { } });

                var fallback = Encoding.UTF8.GetBytes(_lastjson);
                _txchannel.Writer.TryWrite(fallback);
            }
        }

        private void forceoffforrule(Rule r)
        {
            var toturnoff = _prevstate
                .Where(kv => kv.Key.rid == r.Id && kv.Value)
                .Select(kv => kv.Key.ied)
                .Distinct()
                .ToList();

            foreach (var ied in toturnoff)
            {
                _prevstate[(r.Id, ied)] = false;
                _lastjson = JsonMod3.BuildString(iedtoint(ied), makefilterstring(r), false);
                _txchannel.Writer.TryWrite(JsonMod3.BuildBytes(iedtoint(ied), makefilterstring(r), false));
                _countersbyied.AddOrUpdate(ied, 1, (_, v) => v + 1);
            }

            _countersdirty = true;
        }

        private static int iedtoint(string ied)
        {
            if (int.TryParse(ied, out var n)) return n;
            var digits = new string(ied.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out n) ? n : 0;
        }

        private static string formatnumber(double v) =>
            v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        private static string makefilterstring(Rule r)
        {
            string op = r.Operator switch { Operator.Greater => ">", Operator.Less => "<", _ => "=" };
            return $"{r.Parameter} {op} {formatnumber(r.Value)}";
        }

        private static bool tryparsemeasurement(string raw, out Measurement measurement)
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
                if (p.Length >= 4
                    && double.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var pa)
                    && double.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out var pb)
                    && double.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out var pc))
                {
                    measurement = new Measurement(DateTime.Now, p[0].Trim(), pa, pb, pc);
                    return true;
                }
            }
            catch { }

            measurement = default!;
            return false;
        }
    }
}
