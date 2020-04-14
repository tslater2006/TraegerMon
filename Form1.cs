using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Brushes = System.Windows.Media.Brushes;
namespace TraegerMon
{
    public partial class Form1 : Form
    {
        List<TraegerDevice> Devices = null;
        AccountInfo Account = null;
        MqttClient client = null;
        TraegerDevice CurrentDevice = null;
        LineSeries AmbientTempSeries = null;
        LineSeries ProbeTempSeries = null;
        public Form1()
        {
            InitializeComponent();
            InitChart();
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            Account = new AccountInfo();
            WebClient wc = new WebClient();
            wc.Headers["Authorization"] = "Basic " + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(txtUsername.Text + ":" + txtPassword.Text));
            wc.Headers["Content-Type"] = "application/json; charset=UTF-8";
            wc.Headers["User-Agent"] = "Traeger/android";
            wc.Headers["x-dw-client-id"] = System.Environment.GetEnvironmentVariable("TRAEGER_CLIENT_ID",EnvironmentVariableTarget.Machine);
            var authResp = wc.UploadString("https://production-us-traegergrills.demandware.net/s/Sites-Traeger-Site/dw/shop/v17_1/customers/auth", "{\"type\": \"credentials\"}");
            var resp = JObject.Parse(authResp);

            Account.CustomerId = resp["customer_id"].ToString();
            Account.Email = resp["email"].ToString();
            Account.FirstName = resp["first_name"].ToString();
            Account.LastName = resp["last_name"].ToString();

            /* Create traeger.io piece */
            var body = $"{{\"demandwareId\":\"{Account.CustomerId}\",\"email\":\"{Account.Email}\",\"firstname\":\"{Account.FirstName}\",\"lastname\":\"{Account.LastName}\"}}";

            /* generate hmac */
            StringBuilder sb = new StringBuilder();
            sb.Append("POST\n");
            sb.Append("https\n");
            sb.Append("api.traegergrills.io:443\n");
            sb.Append("/v1/user/authenticate\n");
            sb.Append("application/json; charset=UTF-8\n");
            sb.Append("user\n");
            sb.Append(body);
            sb.Append("\n");

            var hmacBody = sb.ToString();
            var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(System.Environment.GetEnvironmentVariable("TRAEGER_HMAC_KEY",EnvironmentVariableTarget.Machine)));
            var hmacString = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(hmacBody)));

            wc = new WebClient();
            wc.Headers["Authorization"] = "HmacSHA256 user:" + hmacString;
            wc.Headers["Content-Type"] = "application/json; charset=UTF-8";
            wc.Headers["User-Agent"] = "okhttp/3.6.0";

            var ioResp = wc.UploadString("https://api.traegergrills.io/v1/user/authenticate",body);

            var resp2 = JObject.Parse(ioResp);
            Account.AccountId = resp2["accountId"].ToString();
            Account.Password = resp2["password"].ToString();

            /* Xively JWT login */
            /* https://id.xively.com/api/v1/auth/login-user */
            wc = new WebClient();
            wc.Headers["Content-Type"] = "application/json; charset=UTF-8";
            wc.Headers["User-Agent"] = "okhttp/3.6.0";

            body = $"{{\"accountId\": \"{Account.AccountId}\",\"emailAddress\": \"{Account.Email}\",	\"password\": \"{Account.Password}\"}}";

            var xivelyAuth = wc.UploadString("https://id.xively.com/api/v1/auth/login-user", body);

            var resp3 = JObject.Parse(xivelyAuth);
            Account.JWT = resp3["jwt"].ToString();

            /* Device List */
            var deviceAPI = $"https://blueprint.xively.com/api/v1/devices?accountId={Account.AccountId}&meta=true&results=true&page=1&pageSize=999";
            wc = new WebClient();
            wc.Headers["Authorization"] = "Bearer " + Account.JWT;
            wc.Headers["Content-Type"] = "application/json; charset=UTF-8";
            wc.Headers["User-Agent"] = "okhttp/3.6.0";

            var deviceJSON = wc.DownloadString(deviceAPI);
            var deviceResp = JObject.Parse(deviceJSON);

            JArray deviceList = deviceResp["devices"].Value<JArray>("results");
            if (deviceList.Count > 0)
            {
                Devices = new List<TraegerDevice>();
                for (var x = 0; x < deviceList.Count; x++)
                {
                    Devices.Add(new TraegerDevice() { ID = deviceList[x]["id"].ToString(), Name = deviceList[x]["name"].ToString() });
                }
            }
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (Account != null)
            {
                lblCustID.Text = Account.CustomerId;
                lblEmail.Text = Account.Email;
                lblFirstName.Text = Account.FirstName;
                lblLastName.Text = Account.LastName;
            }

            cmbDevices.Items.Clear();
            if (Devices != null)
            {
                foreach(var d in Devices)
                {
                    cmbDevices.Items.Add(d.Name);
                }
            }
        }

        private async void ConnectDevice()
        {

            if (cmbDevices.SelectedIndex >= 0)
            {
                CurrentDevice = Devices[cmbDevices.SelectedIndex];
                client = new MqttClient("broker.xively.com", 8883, true, MqttSslProtocols.TLSv1_2, null, null);
                client.Connect(CurrentDevice.ID, "Auth:JWT", Account.JWT);
                client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
                client.Subscribe(new string[] { $"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/grill_data" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                client.Subscribe(new string[] { $"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/_log" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                client.Subscribe(new string[] { $"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/_set/fields" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                client.Subscribe(new string[] { $"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/_updates/fields" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
            }
        }

        private void Client_MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            var topic = e.Topic;
            var msgData = Encoding.UTF8.GetString(e.Message);

            if (e.Topic.EndsWith("_log") || e.Topic.EndsWith("fields"))
            {
                int i = 3;
                Console.WriteLine(msgData);
                return;
            }

            JObject msg = JObject.Parse(msgData);
            JArray elems = msg.Value<JArray>("e");
            JObject data = new JObject();
            for(var x = 0; x < elems.Count; x++)
            {
                data[elems[x].Value<string>("n")] = elems[x].Value<string>("v");
            }

            CurrentDevice.CurrentTemp = data.Value<int>("grill");
            CurrentDevice.SetTemp = data.Value<int>("set");
            CurrentDevice.ProbeTemp = data.Value<int>("probe");
            CurrentDevice.FanLevel = data.Value<int>("fan");
            CurrentDevice.CookTimerEnd = data.Value<long>("cook_timer_end");
            CurrentDevice.SystemTimerEnd = data.Value<long>("sys_timer_end");
            CurrentDevice.SystemStatus = data.Value<int>("system_status");

            AmbientTempSeries.Values.Add(new DataFrame() { Time = DateTime.Now, CurrentTemp = CurrentDevice.CurrentTemp });
            ProbeTempSeries.Values.Add(new DataFrame() { Time = DateTime.Now, CurrentTemp = CurrentDevice.ProbeTemp });
            this.BeginInvoke((MethodInvoker)delegate { UpdateDeviceInfo(); });
        }

        private void UpdateDeviceInfo()
        {
            lblCurrent.Text = CurrentDevice.CurrentTemp + " F";
            lblTarget.Text = CurrentDevice.SetTemp + " F";
            lblProbe.Text = CurrentDevice.ProbeTemp + " F";
            prgFan.Value = CurrentDevice.FanLevel;

            lblSmoke.Text = CurrentDevice.Smoke == 1 ? "Yes" : "No";


            if (CurrentDevice.SystemTimerEnd > 0)
            {
                /* figure out when it is... */
                var timeLeft = DateTimeOffset.FromUnixTimeSeconds(CurrentDevice.SystemTimerEnd).LocalDateTime.Subtract(DateTime.Now);
                lblSystemTimer.Text = timeLeft.ToString(@"dd\.hh\:mm\:ss");
            } else
            {
                lblSystemTimer.Text = "";
            }

            if (CurrentDevice.CookTimerEnd > 0)
            {
                /* figure out when it is... */
                var timeLeft = DateTimeOffset.FromUnixTimeSeconds(CurrentDevice.CookTimerEnd).LocalDateTime.Subtract(DateTime.Now);
                lblCookTimer.Text = timeLeft.ToString(@"dd\.hh\:mm\:ss");
            } else
            {
                lblCookTimer.Text = "";
            }
            
            switch(CurrentDevice.SystemStatus)
            {
                case 2:
                    lblStatus.Text = "Sleeping";
                    break;
                case 3:
                    lblStatus.Text = "Idle";
                    break;
                case 4:
                    lblStatus.Text = "Igniting";
                    break;
                case 5:
                    lblStatus.Text = "Preheating";
                    break;
                case 6:
                    lblStatus.Text = "Manual Cook";
                    break;
                case 7:
                    lblStatus.Text = "Custom Cook";
                    break;
                case 8:
                    lblStatus.Text = "Cooldown";
                    break;
                case 9:
                    lblStatus.Text = "Shutdown";
                    break;
                case 10:
                    lblStatus.Text = "Error";
                    break;
                case 99:
                    lblStatus.Text = "Offline";
                    break;
            }
            /* Device Status */
            /* 2 == Sleep */
            /* 3 == Idle */
            /* 4 == Ignite Status */
            /* 5 == Preheat */
            /* 6 == Manual Cook */
            /* 7 == Custom Cook */
            /* 8 == Cooldown */
            /* 9 == Shutdown */
            /* 10 == Error */
            /* 99 == Offline */

        }

        private void InitChart()
        {
            var dayConfig = Mappers.Xy<DataFrame>()
                .X(model => (double)model.Time.Ticks / TimeSpan.FromHours(1).Ticks)
                .Y(model => model.CurrentTemp);

            AmbientTempSeries = new LineSeries
            {
                Values = new ChartValues<DataFrame>
                {
                },
                PointGeometrySize = 0,
                Title = "Ambient"
            };
            ProbeTempSeries = new LineSeries
            {
                Values = new ChartValues<DataFrame>
                {
                },
                PointGeometrySize = 0,
                Title = "Probe"
            };
            cartesianChart1.Series = new SeriesCollection(dayConfig)
            {
              AmbientTempSeries,
              ProbeTempSeries
            };

            cartesianChart1.AxisX.Add(new Axis
            {
                LabelFormatter = value => new System.DateTime((long)(value * TimeSpan.FromHours(1).Ticks)).ToString("t")
            });

            cartesianChart1.AxisY.Add(new Axis { LabelFormatter = value => value + " F" });
            cartesianChart1.LegendLocation = LegendLocation.Right;
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            ConnectDevice();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            client?.Disconnect();
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            if (CurrentDevice.Smoke == 0)
            {
                client.Publish($"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/run_cmd", Encoding.UTF8.GetBytes("16,165"));
            }else
            {
                client.Publish($"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/run_cmd", Encoding.UTF8.GetBytes("21"));
            }
        }

        private void Button1_Click_1(object sender, EventArgs e)
        {
            client.Publish($"xi/blue/v1/{Account.AccountId}/d/{CurrentDevice.ID}/run_cmd", Encoding.UTF8.GetBytes("1"));
        }
    }
}
