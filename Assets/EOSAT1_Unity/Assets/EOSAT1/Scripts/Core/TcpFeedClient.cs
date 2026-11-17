// EOSAT-1 VR - the live link: TCP, one JSON object per line (simulator port 8100),
// or a WebSocket, one JSON object per message, when Host is a ws:// or wss:// address
// (the course's cloud simulator: wss://ops.c2o.live/vr, ICD smo-vr/1 issue 2 §2).
//
// All socket work happens on a background thread ("never read the socket on the
// render thread"). Parsed frames go into a thread-safe queue that the main thread
// drains once per rendered frame. Once a second the thread sends a status line:
//
//   {"type":"status","status_word":15,"last_seq":1812,...}
//   bit 0 RX_OK      lines arrived in the last 5 s on a live connection
//   bit 1 PARSE_OK   the last line parsed
//   bit 2 RENDERING  the render loop ticked in the last second (a frozen app clears it)
//   bit 3 DATA_FRESH newest state frame under 2 s old
//   bit 4 DEPLOY_SHOWN the wings and antenna are drawn from the deploy fields (set by the app)
//   bit 5 FRAMERATE_OK the render loop is holding its target frame rate (set by the app)
//   bits 8-15        ours (see EosatApp.OwnStatusBits)
// 15 = healthy. A crashed or frozen headset can't look healthy.
// Field names of the status line are a best guess; check VR_INTERFACE_ICD.pdf.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Text;
using System.Threading;

namespace Eosat1.Core
{
    /// <summary>Monotonic wall clock shared by every thread, in seconds.</summary>
    public static class MonoClock
    {
        static readonly Stopwatch watch = Stopwatch.StartNew();

        public static double Now { get { return watch.Elapsed.TotalSeconds; } }
        public static long Ticks { get { return watch.ElapsedTicks; } }
        public static double TicksToSeconds(long ticks) { return ticks / (double)Stopwatch.Frequency; }
    }

    public sealed class TcpFeedClient : IDisposable
    {
        public const int StatusRxOk = 1, StatusParseOk = 2, StatusRendering = 4, StatusDataFresh = 8;
        public const int StatusDeployShown = 0x10, StatusFramerateOk = 0x20;
        /// <summary>Optional bits 4-5 of the status word, set by the main thread.</summary>
        public volatile bool DeployShown, FramerateOk;

        public string Host = "127.0.0.1";
        public int Port = 8100;
        public bool SendStatus = true;
        public double StatusIntervalS = 1.0;
        public double ReconnectDelayS = 2.0;
        public double ConnectTimeoutS = 3.0;
        public string AppName = "EOSAT1-Unity-VR";
        public int MaxLineBytes = 1 << 20;

        readonly ConcurrentQueue<StateFrame> frames = new ConcurrentQueue<StateFrame>();
        readonly ConcurrentQueue<string> log = new ConcurrentQueue<string>();
        Thread thread;
        volatile bool stop;
        volatile Socket socket;
        volatile ClientWebSocket webSocket;

        /// <summary>True for a ws:// or wss:// address (a WebSocket, e.g. the course's cloud simulator).</summary>
        public static bool IsWebSocketAddress(string host)
        {
            return !string.IsNullOrEmpty(host) &&
                   (host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                    host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>How to show an address: "host:port" for TCP, the URL itself for a WebSocket.</summary>
        public static string Describe(string host, int port)
        {
            return IsWebSocketAddress(host) ? host : host + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        // statistics: written by the network thread, read by the main thread
        long linesReceived, framesParsed, parseErrors, bytesReceived, connects, lastSeq = -1;
        long lastLineTicks = long.MinValue, lastFrameTicks = long.MinValue, heartbeatTicks = long.MinValue;
        volatile bool connected, lastParseOk;
        volatile string helloText;
        volatile string schemaProblem;
        volatile int ownBits;
        volatile int lastStatusWord;

        public bool Connected { get { return connected; } }
        public long LinesReceived { get { return Interlocked.Read(ref linesReceived); } }
        public long FramesParsed { get { return Interlocked.Read(ref framesParsed); } }
        public long ParseErrors { get { return Interlocked.Read(ref parseErrors); } }
        public long BytesReceived { get { return Interlocked.Read(ref bytesReceived); } }
        public long Connects { get { return Interlocked.Read(ref connects); } }
        public long LastSeq { get { return Interlocked.Read(ref lastSeq); } }
        public string Hello { get { return helloText; } }
        /// <summary>Set when the server's hello names a protocol version this code doesn't speak; frames are then ignored.</summary>
        public string SchemaProblem { get { return schemaProblem; } }
        public int LastStatusWord { get { return lastStatusWord; } }
        public bool IsRunning { get { return thread != null && thread.IsAlive; } }

        /// <summary>Bits 8-15 of the status word, set by the main thread.</summary>
        public int OwnStatusBits
        {
            get { return ownBits; }
            set { ownBits = value & 0xFF00; }
        }

        public void Start()
        {
            if (IsRunning) return;
            stop = false;
            thread = new Thread(Run) { IsBackground = true, Name = "EOSAT1 TCP feed" };
            thread.Start();
        }

        public void Stop()
        {
            stop = true;
            ClientWebSocket w = webSocket;
            if (w != null)
            {
                try { w.Abort(); }
                catch (Exception) { }
            }
            Socket s = socket;
            if (s != null)
            {
                try { s.Close(); }
                catch (Exception) { }
            }
            if (thread != null && thread.IsAlive) thread.Join(1000);
            thread = null;
            connected = false;
        }

        public void Dispose() { Stop(); }

        /// <summary>Call once per rendered frame from the main thread.</summary>
        public void Heartbeat()
        {
            Interlocked.Exchange(ref heartbeatTicks, MonoClock.Ticks);
        }

        public bool TryDequeue(out StateFrame f) { return frames.TryDequeue(out f); }
        public bool TryDequeueLog(out string line) { return log.TryDequeue(out line); }

        /// <summary>The status word as the network thread would send it right now.</summary>
        public int ComputeStatusWord()
        {
            long now = MonoClock.Ticks;
            int word = 0;
            if (connected && Age(now, Interlocked.Read(ref lastLineTicks)) < 5.0) word |= StatusRxOk;
            if (lastParseOk) word |= StatusParseOk;
            if (Age(now, Interlocked.Read(ref heartbeatTicks)) < 1.0) word |= StatusRendering;
            if (Age(now, Interlocked.Read(ref lastFrameTicks)) < 2.0) word |= StatusDataFresh;
            if (DeployShown) word |= StatusDeployShown;
            if (FramerateOk && (word & StatusRendering) != 0) word |= StatusFramerateOk;
            return word | (ownBits & 0xFF00);
        }

        static double Age(long now, long then)
        {
            if (then == long.MinValue) return double.PositiveInfinity;
            return MonoClock.TicksToSeconds(now - then);
        }

        void Log(string msg)
        {
            if (log.Count < 200) log.Enqueue(msg);
        }

        void Run()
        {
            if (IsWebSocketAddress(Host))
            {
                RunWebSocket();
                return;
            }
            var buffer = new byte[16384];
            var line = new byte[4096];
            int lineLen = 0;
            bool discarding = false;
            while (!stop)
            {
                Socket s = null;
                try
                {
                    IPAddress addr = Resolve(Host);
                    s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    socket = s;
                    IAsyncResult ar = s.BeginConnect(new IPEndPoint(addr, Port), null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(ConnectTimeoutS)))
                        throw new SocketException((int)SocketError.TimedOut);
                    s.EndConnect(ar);
                    s.NoDelay = true;
                    connected = true;
                    Interlocked.Increment(ref connects);
                    lineLen = 0;
                    discarding = false;
                    Log("connected to " + Describe(Host, Port));
                    double nextStatus = MonoClock.Now + StatusIntervalS;
                    while (!stop)
                    {
                        if (s.Poll(100000, SelectMode.SelectRead))
                        {
                            int n = s.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                            if (n <= 0)
                            {
                                Log("the simulator closed the connection");
                                break;
                            }
                            Interlocked.Add(ref bytesReceived, n);
                            for (int k = 0; k < n; k++)
                            {
                                byte c = buffer[k];
                                if (c == (byte)'\n')
                                {
                                    if (!discarding) HandleLine(line, lineLen);
                                    lineLen = 0;
                                    discarding = false;
                                    continue;
                                }
                                if (discarding) continue;
                                if (lineLen == line.Length)
                                {
                                    if (line.Length >= MaxLineBytes)
                                    {
                                        discarding = true;
                                        lineLen = 0;
                                        Interlocked.Increment(ref parseErrors);
                                        lastParseOk = false;
                                        Log("dropped an over-long line");
                                        continue;
                                    }
                                    Array.Resize(ref line, Math.Min(line.Length * 2, MaxLineBytes));
                                }
                                line[lineLen++] = c;
                            }
                        }
                        if (SendStatus && MonoClock.Now >= nextStatus)
                        {
                            SendStatusLine(s);
                            nextStatus = MonoClock.Now + StatusIntervalS;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!stop) Log("link: " + e.Message);
                }
                finally
                {
                    connected = false;
                    socket = null;
                    if (s != null)
                    {
                        try { s.Close(); }
                        catch (Exception) { }
                    }
                }
                double until = MonoClock.Now + ReconnectDelayS;
                while (!stop && MonoClock.Now < until) Thread.Sleep(50);
            }
        }

        /// <summary>
        /// The WebSocket link, on the same background thread: one JSON object per text message,
        /// the status line sent back the same way, reconnecting every ReconnectDelayS when it drops.
        /// </summary>
        void RunWebSocket()
        {
            var buffer = new byte[16384];
            var msg = new byte[4096];
            while (!stop)
            {
                ClientWebSocket ws = null;
                try
                {
                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    webSocket = ws;
                    // the cloud is further away than a laptop on the desk: DNS and TLS take a moment
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(ConnectTimeoutS, 10.0))))
                        ws.ConnectAsync(new Uri(Host), cts.Token).GetAwaiter().GetResult();
                    connected = true;
                    Interlocked.Increment(ref connects);
                    Log("connected to " + Host);
                    double nextStatus = MonoClock.Now + StatusIntervalS;
                    int msgLen = 0;
                    bool discarding = false;
                    Task<WebSocketReceiveResult> pending = null;
                    while (!stop && ws.State == WebSocketState.Open)
                    {
                        if (pending == null) pending = ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (pending.Wait(100))
                        {
                            WebSocketReceiveResult r = pending.Result;
                            pending = null;
                            if (r.MessageType == WebSocketMessageType.Close)
                            {
                                Log("the simulator closed the connection");
                                break;
                            }
                            Interlocked.Add(ref bytesReceived, r.Count);
                            if (!discarding)
                            {
                                if (msgLen + r.Count > MaxLineBytes)
                                {
                                    discarding = true;
                                    Interlocked.Increment(ref parseErrors);
                                    lastParseOk = false;
                                    Log("dropped an over-long message");
                                }
                                else
                                {
                                    if (msgLen + r.Count > msg.Length)
                                        Array.Resize(ref msg, Math.Min(MaxLineBytes, Math.Max(msg.Length * 2, msgLen + r.Count)));
                                    Buffer.BlockCopy(buffer, 0, msg, msgLen, r.Count);
                                    msgLen += r.Count;
                                }
                            }
                            if (r.EndOfMessage)
                            {
                                if (!discarding) HandleMessage(msg, msgLen);
                                msgLen = 0;
                                discarding = false;
                            }
                        }
                        if (SendStatus && MonoClock.Now >= nextStatus)
                        {
                            byte[] data = Encoding.UTF8.GetBytes(BuildStatusLine().TrimEnd('\n'));
                            ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None)
                              .Wait(TimeSpan.FromSeconds(2));
                            nextStatus = MonoClock.Now + StatusIntervalS;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!stop)
                    {
                        Exception inner = e;
                        while (inner.InnerException != null) inner = inner.InnerException;
                        Log("link: " + inner.Message);
                    }
                }
                finally
                {
                    connected = false;
                    webSocket = null;
                    if (ws != null)
                    {
                        try { ws.Abort(); ws.Dispose(); }
                        catch (Exception) { }
                    }
                }
                double until = MonoClock.Now + ReconnectDelayS;
                while (!stop && MonoClock.Now < until) Thread.Sleep(50);
            }
        }

        /// <summary>One WebSocket message: normally one JSON object, but split on newlines just in case.</summary>
        void HandleMessage(byte[] bytes, int len)
        {
            int start = 0;
            for (int k = 0; k <= len; k++)
            {
                if (k == len || bytes[k] == (byte)'\n')
                {
                    if (k > start)
                    {
                        var part = new byte[k - start];
                        Buffer.BlockCopy(bytes, start, part, 0, part.Length);
                        HandleLine(part, part.Length);
                    }
                    start = k + 1;
                }
            }
        }

        static IPAddress Resolve(string host)
        {
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return ip;
            IPAddress[] all = Dns.GetHostAddresses(host);
            foreach (IPAddress a in all)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;
            if (all.Length > 0) return all[0];
            throw new SocketException((int)SocketError.HostNotFound);
        }

        void HandleLine(byte[] bytes, int len)
        {
            int start = 0;
            while (len > 0 && (bytes[len - 1] == (byte)'\r' || bytes[len - 1] == (byte)' ')) len--;
            while (start < len && (bytes[start] == (byte)' ' || bytes[start] == (byte)'\t')) start++;
            if (len - start <= 0) return;
            Interlocked.Increment(ref linesReceived);
            Interlocked.Exchange(ref lastLineTicks, MonoClock.Ticks);
            string text;
            try
            {
                text = Encoding.UTF8.GetString(bytes, start, len - start);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref parseErrors);
                lastParseOk = false;
                return;
            }
            MessageKind kind;
            StateFrame frame;
            string error;
            if (!FrameParser.TryParse(text, out kind, out frame, out error))
            {
                long errs = Interlocked.Increment(ref parseErrors);
                lastParseOk = false;
                if (errs <= 20 || errs % 100 == 0) Log("skipped a line (" + error + ")");
                return;
            }
            lastParseOk = true;
            if (kind == MessageKind.State && frame != null && schemaProblem != null)
            {
                lastParseOk = false;   // refusing to draw frames we can't trust; the status word says so
                return;
            }
            if (kind == MessageKind.State && frame != null)
            {
                frame.ArrivalWall = MonoClock.Now;
                if (frames.Count < 2000) frames.Enqueue(frame);
                Interlocked.Increment(ref framesParsed);
                Interlocked.Exchange(ref lastFrameTicks, MonoClock.Ticks);
                Interlocked.Exchange(ref lastSeq, frame.Seq);
            }
            else if (kind == MessageKind.Hello)
            {
                helloText = text.Length > 240 ? text.Substring(0, 240) + "..." : text;
                Log("hello: " + helloText);
                string schema;
                if (FrameParser.HelloSchemaOk(text, out schema))
                {
                    schemaProblem = null;
                }
                else
                {
                    schemaProblem = "server speaks " + schema + ", this app speaks " + FrameParser.SupportedSchema + ": not drawing its frames";
                    Log("REFUSED: " + schemaProblem);
                }
            }
        }

        void SendStatusLine(Socket s)
        {
            byte[] data = Encoding.UTF8.GetBytes(BuildStatusLine());
            s.Send(data, 0, data.Length, SocketFlags.None);
        }

        string BuildStatusLine()
        {
            int word = ComputeStatusWord();
            lastStatusWord = word;
            string seq = Interlocked.Read(ref lastSeq).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder(220);
            sb.Append("{\"type\":\"status\",\"status_word\":").Append(word.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seq_ack\":").Append(seq);   // the ICD's name: the console shows how many frames behind we are
            sb.Append(",\"last_seq\":").Append(seq);
            sb.Append(",\"frames\":").Append(Interlocked.Read(ref framesParsed).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"parse_errors\":").Append(Interlocked.Read(ref parseErrors).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"app\":\"").Append(AppName).Append("\"}\n");
            return sb.ToString();
        }
    }
}
