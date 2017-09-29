﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using Windows.System.Threading;

//Websocket
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

//https://docs.microsoft.com/zh-tw/windows/uwp/networking/websockets
namespace LiPTT
{
    public class ScreenEventArgs : EventArgs
    {
        public ScreenEventArgs(ScreenBuffer buffer)
        {
            Screen = buffer;
        }

        public ScreenBuffer Screen
        {
            get; set;
        }
    }

    public class PTTClient
    {
        #region 事件一堆堆
        public delegate void ScreenEventHandler(object sender, ScreenEventArgs e);

        /// <summary>
        /// 注意 non-UI thread
        /// </summary>
        public event ScreenEventHandler ScreenUpdated;

        protected void OnScreenUpdated(ScreenBuffer e)
        {
            ScreenUpdated?.Invoke(this, new ScreenEventArgs(e));
        }

        /// <summary>
        /// 注意 non-UI thread
        /// </summary>
        public event ScreenEventHandler ScreenDrawn;

        protected void OnScreenDrawn(ScreenBuffer e)
        {
            ScreenDrawn?.Invoke(this, new ScreenEventArgs(e));
        }

        /// <summary>
        /// 注意 non-UI thread
        /// </summary>
        public event EventHandler Connected;

        protected void OnPTTConnected()
        {
            Debug.WriteLine("PTT Event 已連線");
            Connected?.Invoke(this, new EventArgs());
        }

        /// <summary>
        /// 注意 non-UI thread
        /// </summary>
        public event EventHandler Disconnected;

        protected void OnPTTDisconnected()
        {
            Debug.WriteLine("PTT Event  已中斷連接");
            Disconnected?.Invoke(this, new EventArgs());
        }

        /// <summary>
        /// 注意 non-UI thread
        /// </summary>
        public event EventHandler Kicked;

        protected void OnPTTKicked()
        {
            Debug.WriteLine("PTT Event  被踢下線");
            Kicked?.Invoke(this, new EventArgs());
        }

        /// <summary>
        /// 注意 non-UI thread
        /// </summary>
        public event EventHandler ConnectionFailed;

        protected void OnPTTConnectionFailed()
        {
            Debug.WriteLine("PTT Event  連線失敗");
            ConnectionFailed?.Invoke(this, new EventArgs());
        }

        public event EventHandler Belled;

        protected void OnBellPlayed()
        {
            Debug.WriteLine("燈!");
            Belled?.Invoke(this, new EventArgs());
        }
        #endregion 事件一堆堆

        #region 各種屬性
        public bool Security
        {
            get
            {
                return ConnectionSecurity;
            }
            set
            {
                ConnectionSecurity = value;
            }
        }

        public int Port
        {
            get { return port; }
        }

        public ScreenBuffer Screen
        {
            get
            {
                return screenBuffer;
            }
        }

        public bool IsExit
        {
            get
            {
                return exit;
            }
            set
            {
                exit = value;
            }
        }

        public bool LoginToMany { set; get; }
        #endregion 各種屬性

        private int port;

        private ScreenBuffer screenBuffer;      
        private TcpClient tcpClient;
        private MessageWebSocket WebSocket;

        ThreadPoolTimer TestKickTimer;
        private bool exit;
        private Stream stream;
        private bool ConnectionSecurity;
        protected bool isConnected;
        //public SemaphoreSlim ScreenLocker;
        const int ConnectTimeout = 5 * 1000;
        const int AliveTimeout = 30 * 60 * 1000;

        public PTTClient()
        {
            port = 443;
            DefaultState();
        }

        ~PTTClient()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (tcpClient != null)
            {
                tcpClient.Dispose();
                tcpClient = null;
            }

            if (WebSocket != null)
            {
                WebSocket.Dispose();
                WebSocket = null;
            }

            isConnected = false;
            DefaultState();
        }

        public void Disconnect()
        {
            if (isConnected)
            {
                TestWebSocketRecvTimer?.Cancel();
                isConnected = false;
                Dispose();
                DefaultState();
                OnPTTDisconnected();
                OnScreenUpdated(screenBuffer);
                OnScreenDrawn(screenBuffer);
            }
        }

        public void Connect()
        {
            Task.Run(() =>
            {
                DefaultState();
                LoginToMany = false;
                ConnectPTT();
            });
        }

        private void ConnectPTT()
        {
            if (ConnectionSecurity)
                ConnectWithWebSocket();
            else
                ConnectWithTCP();
        }

        private async void ConnectWithTCP()
        {
            if (tcpClient != null) Disconnect();

            tcpClient = new TcpClient();

            try
            {
                await tcpClient.ConnectAsync("ptt.cc", port);

                if (tcpClient.Connected)
                {
                    TestKickTimer = ThreadPoolTimer.CreatePeriodicTimer((source) => {

                        if (tcpClient?.Client.Poll(10, SelectMode.SelectRead) == true)
                        {
                            TestKickTimer?.Cancel();
                            TestKickTimer = null;

                            if (LoginToMany)
                            {
                                Debug.WriteLine("TCP: 登入太頻繁");
                                Dispose();
                            }
                            if (IsExit)
                            {
                                Disconnect();
                            }
                            else
                            {
                                Dispose();
                                OnPTTKicked();
                            }                              
                        }

                    }, TimeSpan.FromSeconds(1));

                    isConnected = true;
                    OnPTTConnected();
                    Debug.WriteLine("TCP: 已連線");
                    tcpClient.ReceiveTimeout = AliveTimeout;
                    tcpClient.SendTimeout = AliveTimeout;
                    stream = tcpClient.GetStream();
                    StartRecv();
                }
            }
            catch (AggregateException)
            {
                OnPTTConnectionFailed();
            }
        }

        private async void ConnectWithWebSocket()
        {
            WebSocket = new MessageWebSocket();
            WebSocket.Control.MessageType = SocketMessageType.Binary;
            WebSocket.MessageReceived += WebSocket_MessageReceived;
            WebSocket.SetRequestHeader("Origin", "https://www.ptt.cc");

            WebSocket.Closed += (a, e) =>
            {
                if (LoginToMany)
                {
                    Debug.WriteLine("WebSocket: 登入太頻繁");
                    Dispose();
                }
                else if (!IsExit)
                {
                    Dispose();
                    OnPTTKicked();
                }
                else
                {
                    Debug.WriteLine("WebSocket: 連線關閉");
                    Disconnect();
                }
            };

            try
            {
                Uri host = new Uri("wss://ws.ptt.cc/bbs");
                await WebSocket.ConnectAsync(host);
                isConnected = true;
                OnPTTConnected();
                Debug.WriteLine("WebSocket: 已連線");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                OnPTTConnectionFailed();
            }
        }

        Windows.Storage.Streams.Buffer testbuffer = new Windows.Storage.Streams.Buffer(4096);
        ThreadPoolTimer TestWebSocketRecvTimer;
        TimeSpan WebSocketPeriod = TimeSpan.FromSeconds(2);
        MemoryStream ms = new MemoryStream();

        private async void WebSocket_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            IBuffer buf;

            try
            {
                buf = await args.GetDataStream().ReadAsync(testbuffer, testbuffer.Capacity, InputStreamOptions.None);
                if (buf.Length > 0)
                {
                    var bbb = buf.ToArray();
                    await ms.WriteAsync(bbb, 0, bbb.Length);

                    if (buf.Length != 1024)
                    {
                        TestWebSocketRecvTimer?.Cancel();
                        byte[] www = ms.ToArray();
                        ms = new MemoryStream();
                        Parse(www);
                        OnScreenDrawn(screenBuffer);
                        OnScreenUpdated(screenBuffer);
                    }
                    else
                    {
                        TestWebSocketRecvTimer?.Cancel();
                        TestWebSocketRecvTimer = ThreadPoolTimer.CreateTimer(TestWebsocket, WebSocketPeriod);
                    }
                }
            }
            catch(Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
        }

        private void TestWebsocket(ThreadPoolTimer timer)
        {
            byte[] msg = ms.ToArray();
            ms = new MemoryStream();
            Parse(msg);
            OnScreenDrawn(screenBuffer);
            OnScreenUpdated(screenBuffer);
        }

        bool hasNumber;
        bool ESC;
        bool Cut;
        byte[] word;
        bool queue;
        int number;
        Queue<int> numbers;
        byte[] bufOneByte = new byte[1];
        char[] bufOneChar = new char[1];

        private void DefaultState()
        {
            hasNumber = false;
            ESC = false;
            Cut = false;
            word = new byte[2];
            queue = false;
            number = 0;
            numbers = new Queue<int>();
            screenBuffer = new ScreenBuffer();
        }

        byte[] buffer = new byte[1024*16];

        private void StartRecv()
        {
            if (stream == null) return;

            int r = 0;

            while (true)
            {
                try
                {
                   // ScreenLocker.Wait();
                    r = stream.Read(buffer, 0, buffer.Length);
                    if (r > 0)
                    {
                        Parse(buffer, 0, r);
                        //ScreenLocker.Release();
                        OnScreenDrawn(screenBuffer);
                        OnScreenUpdated(screenBuffer);
                    }
                    else
                    {
                        //ScreenLocker.Release();
                    }

                }
                catch (ObjectDisposedException)
                {
                    if (IsExit)
                    {
                        Debug.WriteLine("TCP: 連線關閉");
                        Disconnect();
                    }
                    else
                    {
                        Debug.WriteLine("TCP: 怎麼惹?");
                    }
                    break;
                }
                catch (IOException)
                {
                    Debug.WriteLine("TCP: 連線關閉");
                    Disconnect();
                    break;
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                return isConnected;
            }
        }

        private void Parse(byte[] message)
        {
            Parse(message, 0, message.Length);
        }

        private void Parse(byte[] message, int index, int count)
        {
            if (count <= 0) return;
#if DEBUG
            StringBuilder RAW_Message = new StringBuilder();
#endif
            char last = '\0';

            for (int i = index; i < index + count; i++)
            {
                byte b = message[i];

                if (ESC)
                {
                    if (b >= 0x30 && b <= 0x39) //IsDigit
                    {
                        hasNumber = true;
                        number = 10 * number + (b - 0x30);
                        last = (char)b;
                        continue;
                    }

                    switch (b)
                    {
                        case 0x5B:  // '[':
#if DEBUG
                            RAW_Message.Append("[");
#endif
                            hasNumber = false;
                            number = 0;
                            last = '[';
                            break;
                        case 0x3B:  // ';':
                            if (hasNumber) numbers.Enqueue(number);
#if DEBUG
                            else RAW_Message.Append(";");
#endif
                            if (last == '[')
                            {
                                screenBuffer.DefaultAttribute();
                            }

                            hasNumber = false;
                            number = 0;
                            last = ';';
                            break;
                        case 0x6D:  // 'm': Text Color
                            if (hasNumber) numbers.Enqueue(number);

                            if (numbers.Count == 0)
                            {
                                screenBuffer.DefaultAttribute();
                                screenBuffer.DefaultColor();
#if DEBUG
                                RAW_Message.Append("m");
#endif
                            }
#if DEBUG
                            else
                            {
                                for (int k = 0; k < numbers.Count - 1; k++) RAW_Message.AppendFormat("{0};", numbers.ElementAt(k));
                                RAW_Message.AppendFormat("{0}m", numbers.ElementAt(numbers.Count - 1));
                            }
#endif
                            while (numbers.Count > 0)
                            {
                                int c = numbers.Dequeue();
                                screenBuffer.SetTextColor(c);
                            }
                            hasNumber = false;
                            number = 0;
                            last = 'm';
                            ESC = false;
                            break;
                        case 0x4A:  // 'J': Clear Screen
                            if (hasNumber)
                            {
#if DEBUG
                                RAW_Message.AppendFormat("CLEAR{0}]", number);
#endif
                                screenBuffer.ClearScreen(number);
                            }
                            else
                            {
#if DEBUG
                                RAW_Message.AppendFormat("CLEAR]");
#endif
                                screenBuffer.ClearScreen();
                            }
                            hasNumber = false;
                            number = 0;
                            ESC = false;
                            break;
                        //cursor controls
                        case 0x48:  // 'H': Move Cursor
                        case 0x66:  // 'f': Move Cursor
                            {
                                int row, col;

                                if (numbers.Count > 0) row = numbers.Dequeue();
                                else row = 1;

                                if (hasNumber) col = number;
                                else col = 1;
#if DEBUG
                                RAW_Message.AppendFormat("MOVE{0},{1}]", row, col);
#endif
                                screenBuffer.Move(row, col);
                                hasNumber = false;
                                number = 0;
                                ESC = false;
                            }
                            break;
                        case 0x41:  // 'A': Move Cursor UpLines
#if DEBUG
                            RAW_Message.AppendFormat("UP{0}]", number);
#endif
                            if (hasNumber)
                            {
                                screenBuffer.UpLines(number);
                                hasNumber = false;
                            }
                            number = 0;
                            ESC = false;
                            break;
                        case 0x42:  // 'B': Move Cursor DownLines
#if DEBUG
                            RAW_Message.AppendFormat("DOWN{0}]", number);
#endif
                            if (hasNumber)
                            {
                                screenBuffer.DownLines(number);
                                hasNumber = false;
                            }
                            number = 0;
                            ESC = false;
                            break;
                        case 0x43:  // 'C': Move Cursor Right
#if DEBUG
                            RAW_Message.AppendFormat("RIGHT{0}]", number);
#endif
                            if (hasNumber)
                            {
                                screenBuffer.GoRight(number);
                                hasNumber = false;
                            }
                            number = 0;
                            ESC = false;
                            break;
                        case 0x44:  // 'D': Move Cursor Left
#if DEBUG
                            RAW_Message.AppendFormat("LEFT{0}]", number);
#endif
                            if (hasNumber)
                            {
                                screenBuffer.GoLeft(number);
                                hasNumber = false;
                            }
                            number = 0;
                            ESC = false;
                            break;
                        case 0x52:  // 'R': Current Cursor
#if DEBUG
                            RAW_Message.AppendFormat("CURRENT{0},{1}]", screenBuffer.CurrentX, screenBuffer.CurrentY);
#endif
                            ESC = false;
                            break;
                        case 0x73:  // 's': Save Cursor
#if DEBUG
                            RAW_Message.Append("SAVE]");
#endif
                            screenBuffer.SaveCursor();
                            ESC = false;
                            break;
                        case 0x75:  // 'u': Load Cursor
#if DEBUG
                            RAW_Message.Append("LOAD]");
#endif
                            screenBuffer.LoadCursor();
                            ESC = false;
                            break;
                        //erase functions
                        case 0x4B:  // 'K': Clear Line
                            if (hasNumber)
                            {
#if DEBUG
                                RAW_Message.AppendFormat("CLEARLINE{0}]", number);
#endif
                                screenBuffer.ClearLine(number);
                            }
                            else
                            {
#if DEBUG
                                RAW_Message.Append("CLEARLINE]");
#endif
                                screenBuffer.ClearLine();
                            }
                            hasNumber = false;
                            number = 0;
                            ESC = false;
                            break;
                        case 0x4D: // 'M': Move the cursor up in scrolling region.
#if DEBUG
                            RAW_Message.Append("SCROll UP]");
#endif
                            screenBuffer.UpLines(1);
                            hasNumber = false;
                            number = 0;
                            ESC = false;
                            break;
                        default:
#if DEBUG
                            RAW_Message.AppendFormat("unknown control 0x{0:X}]", b);
#endif
                            hasNumber = false;
                            number = 0;
                            ESC = false;
                            break;
                    }
                }
                else
                {
                    switch (b)
                    {
                        case 0x00: //NUL - null
#if DEBUG
                            RAW_Message.Append("(NUL)");
#endif
                            break;
                        case 0x01: //SOH - start of header
#if DEBUG
                            RAW_Message.Append("(SOH)");
#endif
                            break;
                        case 0x02: //STX - start of text
#if DEBUG
                            RAW_Message.Append("(STX)");
#endif
                            break;
                        case 0x03: //ETX - end of text
#if DEBUG
                            RAW_Message.Append("(ETX)");
#endif
                            break;
                        case 0x04: //EOT - end of transmission
#if DEBUG
                            RAW_Message.Append("(EOT)");
#endif
                            break;
                        case 0x05: //ENQ - enquiry
#if DEBUG
                            RAW_Message.Append("(ENQ)");
#endif
                            break;
                        case 0x06: //ACK - acknowledge
#if DEBUG
                            RAW_Message.Append("(ACK)");
#endif
                            break;
                        case 0x07: //BEL - bell
#if DEBUG
                            RAW_Message.Append("(BEL)");
#endif
                            OnBellPlayed();
                            break;
                        case 0x08: //BS  - backspace
#if DEBUG
                            RAW_Message.Append("(BS)");
#endif
                            screenBuffer.Backspace();
                            break;
                        case 0x09: //HT  - horizontal tab
#if DEBUG
                            RAW_Message.Append("(HT)\t");
#endif
                            break;
                        case 0x0A: //LF  - line feed (enter)
#if DEBUG
                            RAW_Message.Append("(LF)\n");
                            if (screenBuffer.CurrentY == screenBuffer.Height) RAW_Message.Append("[SCROll DOWN]");
#endif
                            screenBuffer.DownLines(1);
                            //screenBuffer.DefaultAttribute();
                            //screenBuffer.DefaultColor();
                            break;
                        case 0x0B: //VT  - vertical tab
#if DEBUG
                            RAW_Message.Append("(VT)");
#endif
                            break;
                        case 0x0C: //FF  - form feed
#if DEBUG
                            RAW_Message.Append("(FF)");
                            RAW_Message.Append('\f');
#endif
                            break;
                        case 0x0D: //CR  - enter/carriage return
#if DEBUG
                            RAW_Message.Append("(CR)");
#endif
                            screenBuffer.CarriageReturn();
                            break;
                        case 0x0E: //SO  - shift out
#if DEBUG
                            RAW_Message.Append("(SO)");
#endif
                            break;
                        case 0x0F: //SI  - shift in
#if DEBUG
                            RAW_Message.Append("(SI)");
#endif
                            break;
                        case 0x10: //DLE - data link escape
#if DEBUG
                            RAW_Message.Append("(DLE)");
#endif
                            break;
                        case 0x11: //DC1 - device control 1
#if DEBUG
                            RAW_Message.Append("(DC1)");
#endif
                            break;
                        case 0x12: //DC2 - device control 2
#if DEBUG
                            RAW_Message.Append("(DC2)");
#endif
                            break;
                        case 0x13: //DC3 - device control 3
#if DEBUG
                            RAW_Message.Append("(DC3)");
#endif
                            break;
                        case 0x14: //DC4 - device control 4
#if DEBUG
                            RAW_Message.Append("(DC4)");
#endif
                            break;
                        case 0x15: //NAK - negative acknowledge
#if DEBUG
                            RAW_Message.Append("(NAK)");
#endif
                            break;
                        case 0x16: //SYN - synchronize
#if DEBUG
                            RAW_Message.Append("(SYN)");
#endif
                            break;
                        case 0x17: //ETB - end of trans. block
#if DEBUG
                            RAW_Message.Append("(BEL)");
#endif
                            break;
                        case 0x18: //CAN - cancel
#if DEBUG
                            RAW_Message.Append("(CAN)");
#endif
                            break;
                        case 0x19: //EM  - end of medium
#if DEBUG
                            RAW_Message.Append("(EM)");
#endif
                            break;
                        case 0x1A: //SUB - substitute
#if DEBUG
                            RAW_Message.Append("(SUB)");
#endif
                            break;
                        case 0x1B: //ESC - escape
                            {
                                if (queue)
                                {
                                    //畫左半個
                                    Cut = true;
#if DEBUG
                                    RAW_Message.AppendFormat("{1}0x{0:X2}{2}", word[0], "{", "}");
#endif
                                    screenBuffer.DrawData(word[0]);
                                }
                                ESC = true;
                            }
                            break;
                        case 0x1C: //FS  - file separator
#if DEBUG
                            RAW_Message.Append("(FS)");
#endif
                            break;
                        case 0x1D: //GS  - group separator
#if DEBUG
                            RAW_Message.Append("(GS)");
#endif
                            break;
                        case 0x1E: //RS  - record separator
#if DEBUG
                            RAW_Message.Append("(RS)");
#endif
                            break;
                        case 0x1F: //US  - unit separator
#if DEBUG
                            RAW_Message.Append("(US)");
#endif
                            break;
                        case 0x7F: //DEL - delete
#if DEBUG
                            RAW_Message.Append("(DEL)");
#endif
                            screenBuffer.DeleteCurrent();
                            break;
                        default: // printable characters
                                 //buf.Add(b);
                            if (Cut)
                            {
                                //畫右半個
#if DEBUG
                                RAW_Message.AppendFormat("{1}0x{0:X2}{2}", b, "{", "}");
#endif
                                Cut = false;
                                queue = false;
                                screenBuffer.DrawData(b);
                            }
                            else
                            {
                                if (queue)
                                {
                                    //沒被切，畫整個
                                    word[1] = b;
#if DEBUG
                                    RAW_Message.Append(LiPTT_Encoding.GetEncoding().GetString(word));
#endif
                                    queue = false;
                                    screenBuffer.DrawData(word);
                                }
                                else if (b < 0x7F)
                                {
                                    if (b == 0x20)
                                    {
#if DEBUG
                                        RAW_Message.Append((char)b);
#endif
                                        screenBuffer.DrawSpace();
                                    }
                                    else
                                    {
#if DEBUG
                                        RAW_Message.Append((char)b);
#endif
                                        screenBuffer.DrawData(b);
                                    }
                                }
                                else
                                {
                                    //先存一半，等等再看
                                    word[0] = b;
                                    queue = true;
                                }
                            }
                            break;
                    }
                }
            }
#if DEBUG
            //Debug.WriteLine(RAW_Message.ToString());
#endif
        }

        public async void Send(byte[] message)
        {
            try
            {
                for (int i = 0; i < message.Length; i++) Debug.WriteLine("==>0x{0:X2}", message[i]);

                if (ConnectionSecurity)
                {
                    await WebSocket.OutputStream.WriteAsync(message.AsBuffer());
                }
                else
                    await stream.WriteAsync(message, 0, message.Length);
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public void Send(string message)
        {
            byte[] msg = LiPTT_Encoding.GetEncoding().GetBytes(message);
            Debug.WriteLine("==>" + message);
            Send(msg);
        }

        public async void Send(char c)
        {
            try
            {
                bufOneChar[0] = c;
                byte[] msg = LiPTT_Encoding.GetEncoding().GetBytes(bufOneChar, 0, 1);

                Debug.WriteLine("==>" + c);

                if (ConnectionSecurity)
                {
                    await WebSocket.OutputStream.WriteAsync(msg.AsBuffer());
                }
                else
                    await stream.WriteAsync(msg, 0, msg.Length);
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public async void Send(byte b)
        {
            try
            {
                bufOneByte[0] = b;
                Debug.WriteLine("==>0x{0:X2}", b);

                if (ConnectionSecurity)
                {
                    await WebSocket.OutputStream.WriteAsync(bufOneByte.AsBuffer());
                }
                else
                    await stream.WriteAsync(bufOneByte, 0, 1);
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public async void Send(params object[] list)
        {
            List<byte> msg = new List<byte>();

            foreach (var o in list)
            {
                if (o.GetType().Equals(typeof(string)))
                {
                    Debug.WriteLine("==>{0}", o);
                    byte[] m = LiPTT_Encoding.GetEncoding().GetBytes((string)o);
                    foreach (var b in m) msg.Add(b);
                }
                else if (o.GetType().Equals(typeof(char)))
                {
                    bufOneChar[0] = (char)o;
                    Debug.WriteLine("==>{0}", o);
                    byte[] m = LiPTT_Encoding.GetEncoding().GetBytes(bufOneChar, 0, 1);
                    foreach (var b in m) msg.Add(b);
                }
                else if (o.GetType().Equals(typeof(int)) || o.GetType().Equals(typeof(short)) || o.GetType().Equals(typeof(byte)))
                {
                    Debug.WriteLine("==>0x{0:X}", o);
                    msg.Add(Convert.ToByte(o));
                }
            }

            byte[] message = msg.ToArray();

            try
            {
                if (ConnectionSecurity)
                {
                    var os = WebSocket.OutputStream;
                    await os.WriteAsync(message.AsBuffer());
                }
                else
                    await stream.WriteAsync(message, 0, message.Length);
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
    }

    /// <summary>
    /// https://blog.gtwang.org/linux/xterm-theme-examples/
    /// </summary>
    [Flags]
    public enum AttributeMode
    {
        None            = 0b0000_0000,
        Bold            = 0b0000_0001,
        LowIntensity    = 0b0000_0010,
        // 0b0000_0100
        Underline       = 0b0000_1000,
        Blink           = 0b0001_0000,
        Reverse         = 0b0100_0000,
        Invisible       = 0b1000_0000,
    }

    public class Block
    {
        public byte Content
        {
            get; set;
        }

        public Block()
        {
            SetDefault();
        }

        public AttributeMode Mode
        {
            get; set;
        }

        public int ForegroundColor
        {
            get; set;
        }

        public SharpDX.Color GetForegroundColor()
        {
            switch (ForegroundColor)
            {
                case 30: //black
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFF808080) :
                        new SharpDX.Color(0xFF000000);
                case 31: //red
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFF0000FF) :
                        new SharpDX.Color(0xFF000080);
                case 32: //green
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFF00FF00) :
                        new SharpDX.Color(0xFF008000);
                case 33: //yellow
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFF00FFFF) :
                        new SharpDX.Color(0xFF008080);
                case 34: //blue
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFFFF0000) :
                        new SharpDX.Color(0xFF800000);
                case 35: //magenta
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFFFF00FF) :
                        new SharpDX.Color(0xFF800080);
                case 36: //cyan
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFFFFFF00) :
                        new SharpDX.Color(0xFF808000);
                case 37: //white
                    return Mode.HasFlag(AttributeMode.Bold) ?
                        new SharpDX.Color(0xFFFFFFFF) :
                        new SharpDX.Color(0xFFC0C0C0);
                default: // dark white
                    return new SharpDX.Color(0xFFC0C0C0);
            }
        }

        public SharpDX.Color GetBackgroundColor()
        {
            switch (BackgroundColor)
            {
                case 40: //black
                    return new SharpDX.Color(0xFF000000);
                case 41: //red
                    return new SharpDX.Color(0xFF000080);
                case 42: //green
                    return new SharpDX.Color(0xFF008000);
                case 43: //yellow
                    return new SharpDX.Color(0xFF008080);
                case 44: //blue
                    return new SharpDX.Color(0xFF800000);
                case 45: //magenta
                    return new SharpDX.Color(0xFF800080);
                case 46: //cyan
                    return new SharpDX.Color(0xFF808000);
                case 47: //white
                    return new SharpDX.Color(0xFFC0C0C0);
                default: //black
                    return new SharpDX.Color(0xFF000000);
            } 
        }

        public int BackgroundColor
        {
            get; set;
        }

        public void SetDefault()
        {
            ForegroundColor = 37;
            BackgroundColor = 40;
            Mode = AttributeMode.None;
            Content = 0;
        }
    }

    public class ScreenBuffer
    {
        private Block[][] Screen;

        private int X;

        private int Y;

        private int temp_x;

        private int temp_y;

        const int delta = 0;

        private int _w;

        private int _h;

        public ScreenBuffer()
        {
            Width = 80;
            Height = 24;
            X = Y = 0;

            Screen = new Block[_h][];

            for (int i = 0; i < _h; i++)
            {
                Screen[i] = new Block[_w];

                for (int j = 0; j < _w; j++)
                {
                    Screen[i][j] = new Block();
                }
            }
        }

        public int CurrentX
        {
            get { return X + 1; }
        }

        public int CurrentY
        {
            get { return Y + 1; }
        }

        public Block CurrentBlock
        {
            get { return Screen[Y][X]; }
            private set
            {
                Screen[Y][X] = value;
            }
        }

        public Block[] CurrentBlocks
        {
            get { return Screen[Y]; }
        }

        private AttributeMode CurrentMode
        {
            get; set;
        }

        private int CurrentForeground
        {
            get; set;
        }

        private int CurrentBackground
        {
            get; set;
        }

        public int Width
        {
            get
            {
                return _w - delta;
            }
            private set
            {
                _w = value + delta;
            }
        }

        public int Height
        {
            get
            {
                return _h - delta;
            }
            private set
            {
                _h = value + delta;
            }
        }

        /// <summary>
        /// 獲得某行字串資訊
        /// </summary>
        /// <param name="row">從0開始</param>
        public string ToString(int row)
        {
            if (row < 0 || row >= Height) return "";
            byte[] mssage = new byte[Width];
            for (int j = 0; j < Width; j++) mssage[j] = Screen[row][j].Content;
            return LiPTT_Encoding.GetEncoding().GetString(mssage);
        }

        /// <summary>
        /// 獲得某行字串資訊
        /// </summary>
        /// <param name="row">列 從0開始</param>
        /// <param name="begin_x">X游標位置</param>
        /// <param name="length">長度</param>
        /// <returns></returns>
        public string ToString(int row, int begin_x, int length)
        {
            if (row < 0 || row >= Height) return "";
            if (begin_x < 0 || begin_x + length - 1 >= Width) return "";
            byte[] mssage = new byte[length];
            for (int j = 0; j < length; j++) mssage[j] = Screen[row][j+ begin_x].Content;
            return LiPTT_Encoding.GetEncoding().GetString(mssage);
        }

        public string ToStringCurrent()
        {
            return ToString(Y, 0, Width);
        }

        /// <summary>
        /// 獲得螢幕字串資訊
        /// </summary>
        public override string ToString()
        {

            byte[] mssage = new byte[(Width + 1)*Height];

            int k = 0;

            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++) mssage[k++] = Screen[i][j].Content;
                mssage[k++] = 0x0A;
            }
 
            return LiPTT_Encoding.GetEncoding().GetString(mssage);
        }

        /// <summary>
        /// 全螢幕資訊
        /// </summary>
        /// <returns></returns>
        public string[] ToStringArray()
        {
            List<string> list = new List<string>();
            byte[] mssage = new byte[Width];
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++) mssage[j] = Screen[i][j].Content;
                list.Add(LiPTT_Encoding.GetEncoding().GetString(mssage));
            }

            return list.ToArray();
        }

        public Block[] this[int line]
        {
            get
            {
                return Screen[line];
            }
        }

        public void ClearLine(int type = 0)
        {
            switch (type)
            {
                case 0: // clear after
                    for (int j = X; j < Width; j++) Screen[Y][j].SetDefault();
                    break;
                case 1: // clear before
                    for (int j = 0; j <= X; j++) Screen[Y][j].SetDefault();
                    break;
                case 2: // clear entire
                    for (int j = 0; j < Width; j++) Screen[Y][j].SetDefault();
                    break;
            }
        }

        public void ClearScreen(int type = 0)
        {
            switch (type)
            {
                case 0: // clear after
                    for (int j = X; j < Width; j++) Screen[Y][j].SetDefault();
                    for (int i = Y + 1; i < Height; i++)
                    {
                        for (int j = 0; j < Width; j++)
                        {
                            Screen[i][j].SetDefault();
                        }
                    }
                    break;
                case 1: // clear before
                    for (int j = 0; j <= X; j++) Screen[Y][j].SetDefault();
                    for (int i = 0; i < Y; i++)
                    {
                        for (int j = 0; j < Width; j++)
                        {
                            Screen[i][j].SetDefault();
                        }
                    }
                    break;
                case 2: // clear entire
                    for (int i = 0; i < Height; i++)
                    {
                        for (int j = 0; j < Width; j++)
                        {
                            Screen[i][j].SetDefault();
                        }
                    }
                    break;
            }
        }

        public void DeleteCurrent()
        {
            Screen[Y][X].SetDefault();
        }

        public void Backspace()
        {
            if (X > 0)
            {
                X--;
            }
            else if (Y > 0)
            {
                Y--;
            }

        }

        public void SaveCursor()
        {
            temp_x = X;
            temp_y = Y;
        }

        public void LoadCursor()
        {
            X = temp_x;
            Y = temp_y;
        }

        public void DownLines(int n)
        {
            if (Y + n >= Height) //Scroll Down
            {
                int r = Y + n - Height + 1;

                if (r < Height)
                {
                    for (int i = 0; i < (Height - r); i++) Screen[i] = Screen[i + r];

                    for (int k = Height - r; k < Height; k++)
                    {
                        Screen[k] = new Block[Width + delta];
                        for (int j = 0; j < Width + delta; j++) Screen[k][j] = new Block();
                    }
                }
                else //clear all
                {
                    ClearScreen(2);
                }
            }
            else
            {
                Y++;
            }
        }

        public void UpLines(int n)
        {
            if (Y == 0) // Scroll Up
            {
                int r = n;

                if (r < Height)
                {
                    for (int i = Height - 1; i >= r; i--) Screen[i] = Screen[i - r];

                    for (int k = 0; k < r; k++)
                    {
                        Screen[k] = new Block[Width + delta];
                        for (int j = 0; j < Width + delta; j++) Screen[k][j] = new Block();
                    }
                }
            }
            else
            {
                Y--;
            }
        }

        public void GoRight(int n)
        {
            if (X + n < Width)
            {
                X += n;
            }
            else
            {
                X = Width - 1;
            }
        }

        public void GoLeft(int n)
        {
            if (X - n >= 0)
            {
                X -= n;
            }
            else
            {
                X = 0;
            }
        }

        public void Move(int row, int col)
        {
            if (row >= 1 && row <= Height)
            {
                Y = row - 1;
            }

            if (col >= 1 && col <= Width)
            {
                X = col - 1;
            }
        }

        public void CarriageReturn()
        {
            X = 0;
        }

        public void SetTextColor(int n)
        {
            switch (n)
            {
                case 0:
                    CurrentMode = AttributeMode.None;
                    DefaultColor();
                    break;
                case 1:
                    CurrentMode |= AttributeMode.Bold;
                    break;
                case 4:
                    CurrentMode |= AttributeMode.Underline;
                    break;
                case 5:
                    CurrentMode |= AttributeMode.Blink;
                    break;
                case 7:
                    CurrentMode |= AttributeMode.Reverse;
                    break;
                case 8:
                    CurrentMode |= AttributeMode.Invisible;
                    break;
                default:
                    if (n >= 30 && n <= 37)
                    {
                        CurrentForeground = n;
                    }
                    else if (n >= 40 && n <= 47)
                    {
                        CurrentBackground = n;
                    }
                    break;
            }
        }

        public void DefaultColor()
        {
            CurrentForeground = 37;
            CurrentBackground = 40;
        }

        public void DefaultAttribute()
        {
            CurrentMode = AttributeMode.None;
        }

        private void NextBlock()
        {
            if (X < Width)
            {
                X++;
            }
            else if (Y < Height)
            {
                X = 0;
                Y++;
            }
        }

        public void DrawData(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                DrawData(data[i]);
            }
        }

        public void DrawData(byte data)
        {
            CurrentBlock.Mode = CurrentMode;
            CurrentBlock.ForegroundColor = CurrentForeground;
            CurrentBlock.BackgroundColor = CurrentBackground;
            CurrentBlock.Content = data;
            NextBlock();
        }

        public void DrawSpace()
        {
            CurrentBlock.Mode = AttributeMode.None;
            CurrentBlock.ForegroundColor = 30;
            CurrentBlock.BackgroundColor = CurrentBackground;
            CurrentBlock.Content = 0x20;
            NextBlock();
        }
    }

}
