using System.Globalization;
using System.IO.Ports;
using System.Net.Http;
using System.Text.Json;

namespace SmartHouseControl;

public sealed class MainForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly SerialPort port = new() { BaudRate = 9600, NewLine = "\n", ReadTimeout = 1000 };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer weatherTimer = new() { Interval = 5 * 60 * 1000 };
    private readonly Random random = new();
    private readonly ComboBox ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly Button connectButton = new() { Text = "연결", Width = 78 };
    private readonly Button refreshButton = new() { Text = "포트 새로고침", Width = 110 };
    private readonly CheckBox demoCheck = new() { Text = "데모 모드", AutoSize = true };
    private readonly CheckBox demoRainCheck = new() { Text = "비 내림 시뮬레이션", AutoSize = true };
    private readonly CheckBox autoRoofCheck = new() { Text = "지붕 자동 제어", Checked = true, AutoSize = true };
    private readonly Button roofOpenButton = new() { Text = "지붕 열기", Width = 105 };
    private readonly Button roofCloseButton = new() { Text = "지붕 닫기", Width = 105 };
    private readonly Button weatherButton = new() { Text = "날씨 즉시 조회", Width = 135 };
    private readonly NumericUpDown lat = new() { DecimalPlaces = 4, Minimum = -90, Maximum = 90, Value = 37.5665m, Width = 92 };
    private readonly NumericUpDown lon = new() { DecimalPlaces = 4, Minimum = -180, Maximum = 180, Value = 126.9780m, Width = 92 };
    private readonly Label weatherLabel = Metric("날씨 조회 전");
    private readonly Label tempLabel = Metric("-- °C");
    private readonly Label rainLabel = Metric("-- mm");
    private readonly Label gasLabel = Metric("-- / 1023");
    private readonly Label distanceLabel = Metric("-- cm");
    private readonly Label doorLabel = Metric("미연결");
    private readonly Label roofLabel = Metric("미연결");
    private readonly Label ventLabel = Metric("미연결");
    private readonly Label statusLabel = new() { AutoSize = true, Text = "아두이노 연결 대기", ForeColor = Color.FromArgb(177, 196, 211) };
    private readonly Label updateLabel = new() { AutoSize = true, Text = "마지막 업데이트: --", ForeColor = Color.FromArgb(177, 196, 211) };
    private readonly TrendGraph gasGraph = new() { Dock = DockStyle.Fill, LineColor = Color.FromArgb(255, 174, 91) };
    private readonly TrendGraph distanceGraph = new() { Dock = DockStyle.Fill, LineColor = Color.FromArgb(83, 187, 229) };
    private readonly TrendGraph tempGraph = new() { Dock = DockStyle.Fill, LineColor = Color.FromArgb(100, 208, 161) };
    private string buffer = "";
    private bool weatherBusy;
    private bool? lastRain;
    private string lastRoofCommand = "";
    private bool simulationRoofClosed;
    private int demoCounter;
    private bool shuttingDown;

    private static readonly Color Bg = Color.FromArgb(15, 23, 34);
    private static readonly Color PanelBg = Color.FromArgb(27, 38, 52);
    private static readonly Color Muted = Color.FromArgb(177, 196, 211);

    public MainForm()
    {
        Text = "SmartHouse Control | 실시간 환경 모니터링";
        ClientSize = new Size(1180, 825);
        MinimumSize = new Size(980, 750);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        BuildUi();
        RefreshPorts();

        refreshButton.Click += (_, _) => RefreshPorts();
        connectButton.Click += (_, _) => ToggleConnection();
        weatherButton.Click += async (_, _) => await GetWeatherAsync();
        roofOpenButton.Click += (_, _) => ManualRoof(false);
        roofCloseButton.Click += (_, _) => ManualRoof(true);
        autoRoofCheck.CheckedChanged += (_, _) =>
        {
            if (autoRoofCheck.Checked && lastRain.HasValue) ApplyRoof(lastRain.Value);
        };
        demoCheck.CheckedChanged += (_, _) =>
        {
            // 데모 모드에서도 실제 Arduino 연결을 유지합니다.
            demoRainCheck.Enabled = demoCheck.Checked;
            UpdateControls();
            if (demoCheck.Checked)
            {
                lastRain = demoRainCheck.Checked;
                weatherLabel.Text = lastRain.Value ? "비 (데모)" : "맑음 (데모)";
                rainLabel.Text = lastRain.Value ? "1.2 mm (데모)" : "0.0 mm (데모)";
                lastRoofCommand = "";
                if (autoRoofCheck.Checked) ApplyRoof(lastRain.Value);
                statusLabel.Text = port.IsOpen
                    ? "날씨 테스트 모드 · Arduino 지붕 모터 제어 활성"
                    : "날씨 테스트 모드 · 실제 모터는 COM 연결 후 작동";
            }
            else
            {
                lastRain = null;
                lastRoofCommand = "";
                weatherLabel.Text = "실제 날씨 조회 대기";
                statusLabel.Text = "실제 날씨 모드 · '날씨 즉시 조회'를 눌러주세요";
            }
        };
        demoRainCheck.Enabled = false;
        demoRainCheck.CheckedChanged += (_, _) =>
        {
            if (demoCheck.Checked)
            {
                lastRain = demoRainCheck.Checked;
                weatherLabel.Text = lastRain.Value ? "비 (데모)" : "맑음 (데모)";
                rainLabel.Text = lastRain.Value ? "1.2 mm (데모)" : "0.0 mm (데모)";
                if (autoRoofCheck.Checked) ApplyRoof(lastRain.Value);
            }
        };
        port.DataReceived += Port_DataReceived;
        updateTimer.Tick += (_, _) =>
        {
            if (demoCheck.Checked && !port.IsOpen) AddDemoMeasurement();
            updateLabel.Text = "현재 시간: " + DateTime.Now.ToString("HH:mm:ss");
        };
        weatherTimer.Tick += async (_, _) =>
        {
            if (!demoCheck.Checked) await GetWeatherAsync();
        };
        updateTimer.Start();
        weatherTimer.Start();
        Shown += async (_, _) => await GetWeatherAsync();
        FormClosing += (_, _) => { shuttingDown = true; updateTimer.Stop(); weatherTimer.Stop(); ClosePort(); };
        UpdateControls();
    }

    private static Label Metric(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("맑은 고딕", 17, FontStyle.Bold),
        ForeColor = Color.White,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true,
        ForeColor = Muted, Font = new Font("맑은 고딕", 9)
    };

    private static FlowLayoutPanel Flow() => new()
    {
        Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
        Padding = new Padding(0, 4, 0, 4), Margin = new Padding(0)
    };

    private static Panel Card() => new()
    {
        BackColor = PanelBg, Padding = new Padding(16), Dock = DockStyle.Fill, Margin = new Padding(6)
    };

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var heading = new Label { Dock = DockStyle.Fill, Text = "SMART HOUSE     /     실시간 환경 모니터링 및 자동 제어", ForeColor = Color.White,
            Font = new Font("맑은 고딕", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(heading, 0, 0);

        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        metrics.Controls.Add(MetricCard("현재 날씨", weatherLabel), 0, 0);
        metrics.Controls.Add(MetricCard("외부 기온 (API)", tempLabel), 1, 0);
        metrics.Controls.Add(MetricCard("강수량 (API)", rainLabel), 2, 0);
        metrics.Controls.Add(MetricCard("가스센서 원시값", gasLabel), 3, 0);
        metrics.Controls.Add(MetricCard("접근 거리", distanceLabel), 4, 0);
        root.Controls.Add(metrics, 0, 1);

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        middle.Controls.Add(GraphCard("가스센서 실시간 그래프 (0~1023, 원시값)", gasGraph), 0, 0);
        middle.Controls.Add(GraphCard("초음파 거리 그래프 (cm)", distanceGraph), 1, 0);
        middle.Controls.Add(GraphCard("외부 기온 그래프 (API 갱신 시)", tempGraph), 0, 1);
        middle.Controls.Add(DevicesCard(), 1, 1);
        root.Controls.Add(middle, 0, 2);

        var settings = Card();
        var settingsTable = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        settingsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        settingsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var first = Flow();
        first.Controls.Add(Caption("Arduino COM 포트  "));
        first.Controls.Add(ports);
        first.Controls.Add(refreshButton);
        first.Controls.Add(connectButton);
        first.Controls.Add(Gap(15));
        first.Controls.Add(demoCheck);
        first.Controls.Add(Gap(15));
        first.Controls.Add(demoRainCheck);
        var second = Flow();
        second.Controls.Add(Caption("위도  "));
        second.Controls.Add(lat);
        second.Controls.Add(Caption(" 경도  "));
        second.Controls.Add(lon);
        second.Controls.Add(weatherButton);
        second.Controls.Add(Gap(13));
        second.Controls.Add(autoRoofCheck);
        second.Controls.Add(roofOpenButton);
        second.Controls.Add(roofCloseButton);
        settingsTable.Controls.Add(first, 0, 0);
        settingsTable.Controls.Add(second, 0, 1);
        settings.Controls.Add(settingsTable);
        root.Controls.Add(settings, 0, 3);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill };
        footer.Controls.Add(statusLabel);
        footer.Controls.Add(Gap(25));
        footer.Controls.Add(updateLabel);
        root.Controls.Add(footer, 0, 4);
    }

    private static Control Gap(int width) => new Panel { Width = width, Height = 10 };

    private static Panel MetricCard(string title, Label value)
    {
        var c = Card();
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(Caption(title), 0, 0);
        content.Controls.Add(value, 0, 1);
        c.Controls.Add(content);
        return c;
    }

    private static Panel GraphCard(string title, TrendGraph graph)
    {
        var c = Card();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold), ForeColor = Color.White }, 0, 0);
        layout.Controls.Add(graph, 0, 1);
        c.Controls.Add(layout);
        return c;
    }

    private Panel DevicesCard()
    {
        var card = Card();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.Controls.Add(new Label { Text = "장치 상태", Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("맑은 고딕", 11, FontStyle.Bold) }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);
        AddDeviceRow(layout, 1, "현관문", doorLabel);
        AddDeviceRow(layout, 2, "종이 지붕", roofLabel);
        AddDeviceRow(layout, 3, "환기 부채", ventLabel);
        card.Controls.Add(layout);
        return card;
    }

    private static void AddDeviceRow(TableLayoutPanel p, int row, string title, Label value)
    {
        p.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted }, 0, row);
        value.Font = new Font("맑은 고딕", 13, FontStyle.Bold);
        p.Controls.Add(value, 1, row);
    }

    private void RefreshPorts()
    {
        var old = ports.SelectedItem?.ToString();
        ports.Items.Clear();
        foreach (string name in SerialPort.GetPortNames().OrderBy(x => x)) ports.Items.Add(name);
        if (old is not null && ports.Items.Contains(old)) ports.SelectedItem = old;
        else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
    }

    private void ToggleConnection()
    {
        if (port.IsOpen) { ClosePort(); return; }
        if (ports.SelectedItem is null) { MessageBox.Show("COM 포트를 먼저 선택해 주세요."); return; }
        try
        {
            port.PortName = ports.SelectedItem.ToString()!;
            buffer = "";
            port.Open();
            lastRoofCommand = "";
            statusLabel.Text = $"연결됨: {port.PortName} (9600 baud)";
            // Arduino MEGA가 USB 연결 시 재시작할 수 있으므로 첫 명령은 약간 늦게 보냅니다.
            var startup = new System.Windows.Forms.Timer { Interval = 2500 };
            startup.Tick += (_, _) =>
            {
                startup.Stop(); startup.Dispose();
                if (!shuttingDown && port.IsOpen && autoRoofCheck.Checked && lastRain.HasValue) ApplyRoof(lastRain.Value);
            };
            startup.Start();
        }
        catch (Exception ex) { MessageBox.Show("연결 실패: " + ex.Message); }
        UpdateControls();
    }

    private void ClosePort()
    {
        try { if (port.IsOpen) port.Close(); } catch { /* unplugged */ }
        if (!shuttingDown) statusLabel.Text = "Arduino 연결 해제";
        if (!shuttingDown) UpdateControls();
    }

    private void UpdateControls()
    {
        connectButton.Text = port.IsOpen ? "연결 해제" : "연결";
        // 날씨 데모와 실제 COM 연결을 함께 사용할 수 있도록 허용
        connectButton.Enabled = true;
        ports.Enabled = !port.IsOpen;
        refreshButton.Enabled = !port.IsOpen;
    }

    private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string incoming = port.ReadExisting();
            if (string.IsNullOrEmpty(incoming)) return;
            BeginInvoke(new Action(() =>
            {
                if (shuttingDown) return;
                buffer += incoming;
                if (buffer.Length > 8192) buffer = "";
                while (true)
                {
                    int index = buffer.IndexOf('\n');
                    if (index < 0) break;
                    string line = buffer[..index].Trim();
                    buffer = buffer[(index + 1)..];
                    ParseLine(line);
                }
            }));
        }
        catch (Exception) { /* Port may be disconnected mid-read. */ }
    }

    private void ParseLine(string line)
    {
        if (demoCheck.Checked) return;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in line.Split(','))
        {
            var pair = token.Split(':', 2);
            if (pair.Length == 2) values[pair[0].Trim()] = pair[1].Trim();
        }
        if (values.TryGetValue("DIST", out var d) && double.TryParse(d, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
        {
            distanceLabel.Text = distance < 0 ? "측정 실패" : $"{distance:0.0} cm";
            if (distance >= 0) distanceGraph.Add(distance);
        }
        if (values.TryGetValue("GAS", out var g) && int.TryParse(g, out var gas))
        {
            gasLabel.Text = $"{gas} / 1023";
            gasGraph.Add(gas);
        }
        if (values.TryGetValue("DOOR", out var door)) doorLabel.Text = door == "OPEN" ? "열림" : "닫힘";
        if (values.TryGetValue("ROOF", out var roof)) roofLabel.Text = roof == "CLOSED" ? "닫힘" : "열림";
        if (values.TryGetValue("VENT", out var vent)) ventLabel.Text = vent == "ON" ? "작동 중" : "정지";
    }

    private void AddDemoMeasurement()
    {
        demoCounter++;
        double distance = 32 + 16 * Math.Sin(demoCounter / 6.0) + random.NextDouble() * 3;
        int gas = (int)(300 + 140 * Math.Sin(demoCounter / 8.0) + random.Next(0, 25));
        gasLabel.Text = $"{gas} / 1023";
        distanceLabel.Text = $"{distance:0.0} cm";
        gasGraph.Add(gas);
        distanceGraph.Add(distance);
        doorLabel.Text = distance <= 20 ? "열림 (데모)" : "닫힘 (데모)";
        ventLabel.Text = gas >= 400 ? "작동 (데모)" : "정지 (데모)";
    }

    private async Task GetWeatherAsync()
    {
        if (weatherBusy || shuttingDown || demoCheck.Checked) return;
        weatherBusy = true;
        weatherButton.Enabled = false;
        try
        {
            string latitude = lat.Value.ToString(CultureInfo.InvariantCulture);
            string longitude = lon.Value.ToString(CultureInfo.InvariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,precipitation,rain,weather_code&timezone=Asia%2FSeoul";
            using var resp = await Http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var current = json.RootElement.GetProperty("current");
            double temperature = current.GetProperty("temperature_2m").GetDouble();
            double rain = current.GetProperty("rain").GetDouble();
            double precipitation = current.GetProperty("precipitation").GetDouble();
            int weatherCode = current.GetProperty("weather_code").GetInt32();
            // 51~67: drizzle/rain; 80~82: showers; 95~99: thunderstorm.
            bool raining = precipitation > 0 || rain > 0 ||
                           (weatherCode >= 51 && weatherCode <= 67) ||
                           (weatherCode >= 80 && weatherCode <= 82) ||
                           (weatherCode >= 95 && weatherCode <= 99);
            lastRain = raining;
            weatherLabel.Text = raining ? "강수 감지" : "강수 없음";
            tempLabel.Text = $"{temperature:0.0} °C";
            rainLabel.Text = $"{precipitation:0.0} mm";
            tempGraph.Add(temperature);
            statusLabel.Text = $"날씨 API 정상 · {DateTime.Now:HH:mm:ss} 갱신 · 지붕 제어 {(autoRoofCheck.Checked ? "자동" : "수동")}";
            if (autoRoofCheck.Checked) ApplyRoof(raining);
        }
        catch (Exception ex)
        {
            weatherLabel.Text = "조회 오류";
            statusLabel.Text = "날씨 연결 오류 — 마지막 지붕 상태 유지: " + ex.Message;
            // 네트워크 오류 시 이전 제어 상태를 유지합니다.
        }
        finally { weatherBusy = false; weatherButton.Enabled = true; }
    }

    private void ApplyRoof(bool close)
    {
        roofLabel.Text = close ? "닫기 명령" : "열기 명령";
        if (demoCheck.Checked)
        {
            simulationRoofClosed = close;
            roofLabel.Text = close ? "닫힘 요청 (데모 날씨)" : "열림 요청 (데모 날씨)";
        }
        // 데모 날씨여도 실제 COM이 연결되면 서보모터 명령을 전송합니다.
        string command = close ? "ROOF_CLOSE" : "ROOF_OPEN";
        if (port.IsOpen && lastRoofCommand != command)
        {
            if (SendCommand(command)) lastRoofCommand = command;
        }
        else if (!port.IsOpen) roofLabel.Text = close ? "닫기 대기 (미연결)" : "열기 대기 (미연결)";
    }

    private void ManualRoof(bool close)
    {
        if (autoRoofCheck.Checked)
        {
            MessageBox.Show("먼저 '지붕 자동 제어' 체크를 해제해 주세요.");
            return;
        }
        ApplyRoof(close);
    }

    private bool SendCommand(string command)
    {
        if (!port.IsOpen) { statusLabel.Text = "아두이노가 연결되지 않았습니다."; return false; }
        try { port.WriteLine(command); return true; }
        catch (Exception ex) { statusLabel.Text = "명령 전송 실패: " + ex.Message; return false; }
    }
}
