using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ntp_time
{
    public partial class Form1 : Form
    {
        private System.Windows.Forms.Timer timer;
        private UdpClient udpClient;
        private ConcurrentQueue<(DateTime[] NetworkTimes, DateTime Timestamp)> networkTimeQueue = new ConcurrentQueue<(DateTime[], DateTime)>();
        private DateTime lastUpdate = DateTime.MinValue;

        public Form1()
        {
            InitializeComponent();
            udpClient = new UdpClient(); // 只创建一个UDP客户端
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 10; // 10毫秒
            timer.Tick += Timer_Tick;
            timer.Start();
            MessageBox.Show("注意！请勿长时间使用！\n有可能被判定为dos攻击\n因为原理就是每10ms请求一次各个ntp服务器");
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            string[] ntpServers = new string[]
            {
                "ntp.ntsc.ac.cn", "cn.pool.ntp.org", "time.windows.com",
                "ntp.aliyun.com", "ntp.tencent.com", "time1.google.com",
                "pool.ntp.org", "time.cloudflare.com", "time.apple.com"
            };

            Task<DateTime>[] tasks = new Task<DateTime>[ntpServers.Length];
            for (int i = 0; i < ntpServers.Length; i++)
            {
                tasks[i] = GetNetworkTimeAsync(ntpServers[i]);
            }

            DateTime[] networkTimes = await Task.WhenAll(tasks);

            // 使用当前时间戳标记数据
            networkTimeQueue.Enqueue((networkTimes, DateTime.UtcNow));

            // 使用Invoke在UI线程上更新控件
            this.Invoke(new Action(UpdateUI));
        }

        private void UpdateUI()
        {
            DateTime[] networkTimes;
            DateTime timestamp;

            // 处理最新的时间戳
            while (networkTimeQueue.TryDequeue(out var result))
            {
                (networkTimes, timestamp) = result;

                if (timestamp > lastUpdate)
                {
                    lastUpdate = timestamp;

                    textBox1.Text = networkTimes[0].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox2.Text = networkTimes[1].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox3.Text = networkTimes[2].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox4.Text = networkTimes[3].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox5.Text = networkTimes[4].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox6.Text = networkTimes[5].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox7.Text = networkTimes[6].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox8.Text = networkTimes[7].ToString("yyyy-MM-dd HH:mm:ss.fff");
                    textBox9.Text = networkTimes[8].ToString("yyyy-MM-dd HH:mm:ss.fff");
                }
            }
        }

        private void SetSystemTimeFromTextBox(TextBox textBox)
        {
            if (DateTime.TryParse(textBox.Text, out DateTime newSystemTime))
            {
                DateTime utcTime = newSystemTime.ToUniversalTime();
                SetSystemTime(utcTime);
            }
        }

        private void button1_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox1);
        private void button2_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox2);
        private void button3_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox3);
        private void button4_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox4);
        private void button5_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox5);
        private void button6_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox6);
        private void button7_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox7);
        private void button8_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox8);
        private void button9_Click(object sender, EventArgs e) => SetSystemTimeFromTextBox(textBox9);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME st);

        private static void SetSystemTime(DateTime dt)
        {
            SYSTEMTIME st = new SYSTEMTIME
            {
                wYear = (ushort)dt.Year,
                wMonth = (ushort)dt.Month,
                wDay = (ushort)dt.Day,
                wHour = (ushort)dt.Hour,
                wMinute = (ushort)dt.Minute,
                wSecond = (ushort)dt.Second,
                wMilliseconds = (ushort)dt.Millisecond
            };
            SetSystemTime(ref st);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        private async Task<DateTime> GetNetworkTimeAsync(string ntpServer)
        {
            const int ntpDataLength = 48;
            byte[] ntpData = new byte[ntpDataLength];
            ntpData[0] = 0x1B;

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(ntpServer);
            IPEndPoint ipEndPoint = new IPEndPoint(addresses[0], 123);

            try
            {
                // 复用UDP客户端
                await udpClient.SendAsync(ntpData, ntpData.Length, ipEndPoint);
                UdpReceiveResult result = await udpClient.ReceiveAsync();
                ntpData = result.Buffer;
            }
            catch (Exception ex)
            {
                // 处理异常情况
                // MessageBox.Show(ex.Message);
            }

            ulong intPart = SwapEndianness(BitConverter.ToUInt32(ntpData, 40));
            ulong fracPart = SwapEndianness(BitConverter.ToUInt32(ntpData, 44));

            ulong seconds = intPart;
            ulong fraction = fracPart;

            DateTime networkDateTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((long)seconds);

            const double ntpFractionalUnit = 1.0 / (1L << 32);
            double fractionInSeconds = fraction * ntpFractionalUnit;
            TimeSpan preciseTimeSpan = TimeSpan.FromSeconds(fractionInSeconds);

            return networkDateTime.Add(preciseTimeSpan).ToLocalTime();
        }

        private static uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) +
                          ((x & 0x0000ff00) << 8) +
                          ((x & 0x00ff0000) >> 8) +
                          ((x & 0xff000000) >> 24));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            udpClient?.Close(); // 关闭UDP客户端，释放资源
            base.OnFormClosed(e);
        }
    }
}
