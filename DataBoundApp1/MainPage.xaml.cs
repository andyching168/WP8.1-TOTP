using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Security.Cryptography;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Coding4Fun.Toolkit.Controls;
using System.Windows.Input;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json.Linq;
using Windows.Web.Http;
using System.IO.IsolatedStorage;
using System.Collections.ObjectModel;
using System.Linq; // 確保引入 Linq 命名空間
using Newtonsoft.Json;
using System.ComponentModel;
using System.Windows.Controls;
namespace DataBoundApp1
{
    public partial class MainPage : PhoneApplicationPage
    {
        public class TotpItem : INotifyPropertyChanged
        {
            private string _name;
            private string _base32Key;
            private byte[] _keyBytes;
            private string _otp;
            private int _remainingSeconds = 30; // 剩餘秒數

            public string Name
            {
                get { return _name; }
                set
                {
                    if (_name != value)
                    {
                        _name = value;
                        OnPropertyChanged("Name");
                    }
                }
            }

            public string Base32Key
            {
                get { return _base32Key; }
                set
                {
                    if (_base32Key != value)
                    {
                        _base32Key = value;
                        OnPropertyChanged("Base32Key");
                    }
                }
            }

            [JsonIgnore]
            public byte[] KeyBytes
            {
                get { return _keyBytes; }
                set
                {
                    if (_keyBytes != value)
                    {
                        _keyBytes = value;
                        OnPropertyChanged("KeyBytes");
                    }
                }
            }

            public string Otp
            {
                get { return _otp; }
                set
                {
                    if (_otp != value)
                    {
                        _otp = value;
                        OnPropertyChanged("Otp");
                    }
                }
            }

            public string GetOtp(int timeDeviation)
            {
                if (KeyBytes != null)
                {
                    return MainPage.GenerateTotp(KeyBytes, timeDeviation);
                }
                return "無效金鑰";
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
                }
            }
        }

        private int _timeDeviation = 0; // 時間偏移量
        private DispatcherTimer _timer; // 用於每秒更新密碼和進度條
        private int _remainingSeconds = 30; // 剩餘秒數
        private DatagramSocket _socket; // 將 DatagramSocket 宣告為類別層級變數
        private ObservableCollection<TotpItem> _totpList = new ObservableCollection<TotpItem>();
        //在此處填上自建QRCode解碼Server，具體可查看https://hub.docker.com/repository/docker/andyching168/fastapi-qr/general
        private static string qrDecodeAPI_URL = "";


        private void ShowLoading(string message)
        {
            SystemTray.ProgressIndicator = new ProgressIndicator
            {
                IsVisible = true,
                IsIndeterminate = true,
                Text = message
            };
        }

        private void HideLoading()
        {
            if (SystemTray.ProgressIndicator != null)
            {
                SystemTray.ProgressIndicator.IsVisible = false;
            }
        }
        private async void FetchNetworkTime()
        {
            try
            {
                // 使用 HttpClient 獲取伺服器時間
                using (HttpClient client = new HttpClient())
                {
                    // timeapi.io 的 API URL
                    string url = "https://timeapi.io/api/Time/current/zone?timeZone=UTC";
                    ShowLoading("時間校正中...");
                    HttpResponseMessage response = await client.GetAsync(new Uri(url));

                    if (response.IsSuccessStatusCode)
                    {
                        // 解析伺服器返回的 JSON 資料
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        JObject json = JObject.Parse(jsonResponse);

                        // 獲取伺服器時間
                        string utcDateTimeString = json["dateTime"].ToString();
                        DateTime serverTime = DateTime.Parse(utcDateTimeString).ToLocalTime();

                        // 計算與本地時間的差異並四捨五入為整數秒
                        TimeSpan timeDifference = serverTime - DateTime.Now;
                        int roundedTimeDifference = (int)Math.Round(timeDifference.TotalSeconds);

                        // 更新 _timeDeviation 並保存
                        _timeDeviation = roundedTimeDifference;
                        IsolatedStorageSettings.ApplicationSettings["TimeDeviation"] = _timeDeviation;
                        IsolatedStorageSettings.ApplicationSettings.Save();
                        HideLoading();
                        // 刷新滑條和 TimeDeviationValue 的顯示
                        Dispatcher.BeginInvoke(() =>
                        {
                            TimeDeviationAdj.Value = _timeDeviation;
                            TimeDeviationValue.Text = _timeDeviation.ToString();
                            MessageBox.Show("伺服器時間：" + serverTime +
                                            "\n本地時間：" + DateTime.Now +
                                            "\n時間差異：" + timeDifference +
                                            "\n已更新偏差值為：" + _timeDeviation);
                        });
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            HideLoading();
                            MessageBox.Show("無法從伺服器獲取時間，請檢查網路連線。");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    HideLoading();
                    MessageBox.Show("獲取伺服器時間失敗：" + ex.Message);
                });
            }
        }

        public MainPage()
        {
            InitializeComponent();

            // 從儲存中讀取偏差值
            if (IsolatedStorageSettings.ApplicationSettings.Contains("TimeDeviation"))
            {
                _timeDeviation = (int)IsolatedStorageSettings.ApplicationSettings["TimeDeviation"];
                TimeDeviationAdj.Value = _timeDeviation; // 更新滑條
                TimeDeviationValue.Text = _timeDeviation.ToString(); // 更新標籤
            }

            // 載入儲存的金鑰
            LoadKeysFromStorage();
            // 監聽滑條值變化事件

            TimeDeviationAdj.ValueChanged += TimeDeviationAdj_ValueChanged;
            TOTP_List.ItemsSource = _totpList;
            TOTP_List.SelectionChanged += TOTP_List_SelectionChanged;
            CreateApplicationBar();
            // 延遲Timer啟動，確保資料先載入
            Dispatcher.BeginInvoke(() =>
            {
                Task.Run(async () =>
                {
                    await Task.Delay(500); // 延遲500毫秒
                    Dispatcher.BeginInvoke(() =>
                    {
                        // 初始化計時器
                        _timer = new DispatcherTimer();
                        _timer.Interval = TimeSpan.FromSeconds(1);
                        _timer.Tick += Timer_Tick;
                        _timer.Start();

                        // 初次開啟時顯示 TOTP 密碼
                        if (_totpList.Count == 0)
                        {
                            TotpTextBlock.Text = "請新增金鑰";
                            TimeProgressBar.Value = 0;
                            return;
                        }
                        else if (_totpList[0].KeyBytes == null)
                        {
                            TotpTextBlock.Text = "金鑰格式無效！";
                            TimeProgressBar.Value = 0;
                            return;
                        }
                        else
                        {
                            TotpTextBlock.Text = string.Format("還剩: {0}秒", _remainingSeconds);
                        }
                    });
                });
            });
        }

        private void TOTP_List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            TotpItem selectedItem = TOTP_List.SelectedItem as TotpItem;

            if (selectedItem != null)
            {
                // 顯示選項對話框
                var messagePrompt = new MessagePrompt
                {
                    Title = "選項",
                    Message = "您選擇了項目："+selectedItem.Name+"\nTOTP 密碼："+selectedItem.GetOtp(_timeDeviation),
                    IsCancelVisible = false
                };

                // 新增刪除按鈕
                var deleteButton = new Button
                {
                    Content = "刪除",
                    Margin = new Thickness(5)
                };
                deleteButton.Click += (s, args) =>
                {
                    // 確認刪除
                    var result = MessageBox.Show("確定要刪除"+selectedItem.Name+"嗎？", "確認刪除", MessageBoxButton.OKCancel);
                    if (result == MessageBoxResult.OK)
                    {
                        // 從清單中移除項目
                        _totpList.Remove(selectedItem);

                        // 儲存更新後的清單
                        SaveKeysToStorage();

                        MessageBox.Show(selectedItem.Name + " 已刪除！");
                    }

                    // 關閉對話框
                    messagePrompt.Hide();
                };

                // 新增刪除按鈕到對話框
                messagePrompt.ActionPopUpButtons.Add(deleteButton);

                // 顯示對話框
                messagePrompt.Show();

                // 清除選擇，避免重複觸發
                TOTP_List.SelectedItem = null;
            }
        }

        private void TimeDeviationAdj_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 更新偏差值顯示
            _timeDeviation = (int)e.NewValue;
            TimeDeviationValue.Text = _timeDeviation.ToString();

            // 將偏差值保存到儲存中
            IsolatedStorageSettings.ApplicationSettings["TimeDeviation"] = _timeDeviation;
            IsolatedStorageSettings.ApplicationSettings.Save();
        }

        private void ShowInputPrompt()
        {
            var inputPrompt = new InputPrompt
            {
                Title = "輸入金鑰",
                Message = "請輸入您的金鑰 (Base32 格式)：",
                InputScope = new InputScope()
            };

            // 設置 InputScope 的 NameValue
            var inputScopeName = new InputScopeName();
            inputScopeName.NameValue = InputScopeNameValue.Default;
            inputPrompt.InputScope.Names.Add(inputScopeName);

            inputPrompt.Completed += InputPrompt_Completed;
            inputPrompt.Show();
        }

        private void InputPrompt_Completed(object sender, PopUpEventArgs<string, PopUpResult> e)
        {
            if (e.PopUpResult == PopUpResult.Ok && !string.IsNullOrEmpty(e.Result))
            {
                var inputKey = e.Result;
                try
                {
                    // 將 Base32 金鑰轉換為位元組陣列
                    var keyBytes = Base32Decode(inputKey);

                    // 顯示名稱輸入框
                    var namePrompt = new InputPrompt
                    {
                        Title = "輸入名稱",
                        Message = "請為此金鑰輸入一個名稱：",
                        InputScope = new InputScope()
                    };

                    namePrompt.Completed += new EventHandler<PopUpEventArgs<string, PopUpResult>>((s, nameEvent) =>
                    {
                        if (nameEvent.PopUpResult == PopUpResult.Ok && !string.IsNullOrEmpty(nameEvent.Result))
                        {
                            var inputName = nameEvent.Result;

                            // 新增到清單
                            var newItem = new TotpItem { Name = inputName, Base32Key = inputKey, KeyBytes = keyBytes };
                            _totpList.Add(newItem);

                            // 儲存到 IsolatedStorage
                            SaveKeysToStorage();

                            MessageBox.Show("已新增金鑰：" + inputName);
                        }
                        else
                        {
                            MessageBox.Show("您未輸入名稱！");
                        }
                    });

                    namePrompt.Show();
                }
                catch (Exception)
                {
                    MessageBox.Show("金鑰格式無效！");
                }
            }
            else
            {
                MessageBox.Show("您未輸入任何金鑰！");
            }
        }

        private string SerializeTotpList(ObservableCollection<TotpItem> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in list)
            {
                sb.Append(item.Name).Append("|").Append(item.Base32Key).Append(";");
            }
            return sb.ToString();
        }

        private ObservableCollection<TotpItem> DeserializeTotpList(string data)
        {
            ObservableCollection<TotpItem> list = new ObservableCollection<TotpItem>();
            if (!string.IsNullOrEmpty(data))
            {
                string[] items = data.Split(';');
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item))
                    {
                        string[] parts = item.Split('|');
                        if (parts.Length == 2)
                        {
                            try
                            {
                                string name = parts[0];
                                string base32Key = parts[1];
                                byte[] keyBytes = Base32Decode(base32Key);
                                list.Add(new TotpItem { Name = name, Base32Key = base32Key, KeyBytes = keyBytes });
                            }
                            catch (FormatException)
                            {
                                // 處理 Base32 金鑰格式錯誤
                                System.Diagnostics.Debug.WriteLine("Base32 金鑰格式錯誤，已忽略。");
                            }
                        }
                    }
                }
            }
            return list;
        }

        private void SaveKeysToStorage()
        {
            try
            {
                string serializedData = SerializeTotpList(_totpList);
                IsolatedStorageSettings.ApplicationSettings["TotpItems"] = serializedData;
                IsolatedStorageSettings.ApplicationSettings.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("儲存金鑰失敗: " + ex.Message);
                Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show("儲存金鑰失敗，請稍後重試。");
                });
            }
        }

        private void LoadKeysFromStorage()
        {
            try
            {
                if (IsolatedStorageSettings.ApplicationSettings.Contains("TotpItems"))
                {
                    string serializedData = IsolatedStorageSettings.ApplicationSettings["TotpItems"] as string;
                    ObservableCollection<TotpItem> loadedList = DeserializeTotpList(serializedData);

                    // 在 UI 執行緒上更新 _totpList
                    Dispatcher.BeginInvoke(() =>
                    {
                        _totpList.Clear(); // 清空現有的 _totpList
                        if (loadedList != null)
                        {
                            foreach (var item in loadedList)
                            {
                                _totpList.Add(item);
                            }
                        }

                        //手動觸發Timer_Tick
                        if (_totpList.Count > 0 && _totpList[0].KeyBytes != null)
                        {
                            TotpTextBlock.Text = string.Format("TOTP 密碼: {0}", GenerateTotp(_totpList[0].KeyBytes, _timeDeviation));
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("載入金鑰失敗: " + ex.Message);
                Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show("載入金鑰失敗，請稍後重試。");
                });
            }
        }

        private void CreateApplicationBar()
        {
            // 建立 Application Bar
            ApplicationBar = new ApplicationBar();
            ApplicationBar.IsVisible = true;
            ApplicationBar.IsMenuEnabled = true;

            // 新增按鈕
            var addButton = new ApplicationBarIconButton(new Uri("/Assets/AppBar/add.png", UriKind.Relative))
            {
                Text = "新增"
            };
            addButton.Click += AddButton_Click;
            ApplicationBar.Buttons.Add(addButton);
            

            var qrButton = new ApplicationBarIconButton(new Uri("/Assets/AppBar/feature.camera.png", UriKind.Relative))
            {
                Text = "QRCode"
            };
            qrButton.Click += qrButton_Click;
            
            if (qrDecodeAPI_URL != "")
            {
                ApplicationBar.Buttons.Add(qrButton);
            }
            var timeSync = new ApplicationBarMenuItem("時間校正");
            timeSync.Click += timeSync_Click;
            ApplicationBar.MenuItems.Add(timeSync);
        }
        private Dictionary<string, string> ParseQueryString(string query)
        {
            var queryDictionary = new Dictionary<string, string>();
            var queryParts = query.TrimStart('?').Split('&');

            foreach (var part in queryParts)
            {
                var keyValue = part.Split('=');
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0]);
                    var value = Uri.UnescapeDataString(keyValue[1]);
                    queryDictionary[key] = value;
                }
            }

            return queryDictionary;
        }
        public class QrCodeResult
        {
            public List<Symbol> Symbol { get; set; }
        }

        public class Symbol
        {
            public string Data { get; set; }
            public string Error { get; set; }
        }

        
        private async Task<string> AnalyzeImageWithCustomApi(System.IO.Stream imageStream)
        {
            try
            {
                // 將圖片轉換為 Base64 編碼
                byte[] imageBytes;
                using (var memoryStream = new MemoryStream())
                {
                    imageStream.CopyTo(memoryStream);
                    imageBytes = memoryStream.ToArray();
                }
                string base64Image = Convert.ToBase64String(imageBytes);

                // 構建請求 JSON
                string requestJson = string.Format(@"
                {{
                    ""image_base64"": ""{0}""
                }}", base64Image);

                // 發送 HTTP POST 請求到自建 API
                var httpRequest = (HttpWebRequest)WebRequest.Create(qrDecodeAPI_URL);
                httpRequest.Method = "POST";
                httpRequest.ContentType = "application/json";

                using (var streamWriter = new StreamWriter(await httpRequest.GetRequestStreamAsync()))
                {
                    streamWriter.Write(requestJson);
                }

                // 獲取響應
                var httpResponse = (HttpWebResponse)await httpRequest.GetResponseAsync();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    string responseJson = await streamReader.ReadToEndAsync();

                    // 解析返回的 JSON
                    var jsonResponse = Newtonsoft.Json.Linq.JObject.Parse(responseJson);
                    var qrCodes = jsonResponse["qr_codes"];

                    if (qrCodes != null && qrCodes.HasValues)
                    {
                        return qrCodes[0].ToString(); // 返回第一個解碼的 QR Code
                    }
                    else
                    {
                        MessageBox.Show("無法解碼 QR Code！");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("自建 API 調用失敗：" + ex.Message);
                return null;
            }
        }
        
        
        private async void qrButton_Click(object sender, EventArgs e)
        {
            try
            {
                var photoChooserTask = new Microsoft.Phone.Tasks.PhotoChooserTask();
                photoChooserTask.Completed += async (s, result) =>
                {
                    if (result.TaskResult == Microsoft.Phone.Tasks.TaskResult.OK && result.ChosenPhoto != null)
                    {
                        try
                        {
                            ShowLoading("QR Code辨識中...");
                            var scannedKey = await AnalyzeImageWithCustomApi(result.ChosenPhoto);
                            HideLoading();
                            if (!string.IsNullOrEmpty(scannedKey) && scannedKey.StartsWith("otpauth://totp/"))
                            {
                                try
                                {
                                    // 解析 otpauth URL
                                    Uri uri = new Uri(scannedKey);
                                    string name = Uri.UnescapeDataString(uri.AbsolutePath.Substring(1)); // 提取名稱
                                    var queryParams = ParseQueryString(uri.Query);
                                    string secret = queryParams.ContainsKey("secret") ? queryParams["secret"] : null;

                                    if (string.IsNullOrEmpty(secret))
                                    {
                                        MessageBox.Show("無效的 QR Code：缺少 secret 參數！");
                                        return;
                                    }

                                    // 如果名稱為空，提示使用者輸入名稱
                                    if (string.IsNullOrEmpty(name))
                                    {
                                        var namePrompt = new InputPrompt
                                        {
                                            Title = "輸入名稱",
                                            Message = "此金鑰缺少名稱，請輸入一個名稱：",
                                            InputScope = new InputScope()
                                        };

                                        namePrompt.Completed += (nameSender, nameEvent) =>
                                        {
                                            if (nameEvent.PopUpResult == PopUpResult.Ok && !string.IsNullOrEmpty(nameEvent.Result))
                                            {
                                                name = nameEvent.Result;

                                                // 新增到清單
                                                AddTotpItem(name, secret);
                                            }
                                            else
                                            {
                                                MessageBox.Show("您未輸入名稱！");
                                            }
                                        };

                                        namePrompt.Show();
                                    }
                                    else
                                    {
                                        // 新增到清單
                                        AddTotpItem(name, secret);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("無效的 QR Code 格式！\n錯誤：" + ex.Message);
                                }
                            }
                            else
                            {
                                MessageBox.Show("掃描結果不是有效的 otpauth URL！\n"+scannedKey);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("解碼失敗：" + ex.Message);
                        }
                    }
                    else
                    {
                        MessageBox.Show("未選擇圖片！");
                    }
                };

                photoChooserTask.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("掃描失敗：" + ex.Message);
            }
        }
        private void AddTotpItem(string name, string secret)
        {
            try
            {
                // 將 Base32 金鑰轉換為位元組陣列
                var keyBytes = Base32Decode(secret);

                // 新增到清單
                var newItem = new TotpItem { Name = name, Base32Key = secret, KeyBytes = keyBytes };
                _totpList.Add(newItem);

                // 儲存到 IsolatedStorage
                SaveKeysToStorage();

                MessageBox.Show("已新增金鑰："+name);
            }
            catch (Exception ex)
            {
                MessageBox.Show("新增金鑰失敗："+ex.Message);
            }
        }
        private async void timeSync_Click(object sender, EventArgs e)
        {
            FetchNetworkTime();
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            ShowInputPrompt();
            // 從輸入框中獲取金鑰
        }
        public static string GenerateTotp(byte[] key, int timeDeviation)
        {
           // 計算當前時間步數 (30 秒為一個步數)
            var timestep = (GetUnixTimeSeconds(timeDeviation)) / 30;

            // 將時間步數轉為 8 位元組的 big-endian 格式
            var timestepBytes = BitConverter.GetBytes(timestep);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(timestepBytes);
            }

            // 使用 HMAC-SHA1 計算雜湊值
            using (var hmac = new HMACSHA1(key))
            {
                var hash = hmac.ComputeHash(timestepBytes);

                // 動態截取 (Dynamic Truncation)
                var offset = hash[hash.Length - 1] & 0x0F;
                var binaryCode = (hash[offset] & 0x7F) << 24
                            | (hash[offset + 1] & 0xFF) << 16
                            | (hash[offset + 2] & 0xFF) << 8
                            | (hash[offset + 3] & 0xFF);

                // 取最後 6 位數字
                var totp = binaryCode % 1000000;
                return totp.ToString("D6");
            }
        }
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_totpList.Count == 0)
            {
                TotpTextBlock.Text = "請新增金鑰";
                TimeProgressBar.Value = 0;
                return;
            }

            foreach (var item in _totpList)
            {
                // 更新每個項目的 Otp 屬性
                item.Otp = item.GetOtp(_timeDeviation);
            }

            // 計算當前時間步數 (30 秒為一個步數)，並應用時間偏差
            long currentUnixTime = GetUnixTimeSeconds(_timeDeviation);
            var secondsInCurrentStep = currentUnixTime % 30;
            _remainingSeconds = 30 - Convert.ToInt32(secondsInCurrentStep) ;

            NowTime.Text = string.Format("校正時間: {0}", DateTime.Now.AddSeconds(_timeDeviation).ToString("HH:mm:ss"));
            SysTime.Text = string.Format("系統時間: {0}", DateTime.Now.ToString("HH:mm:ss"));
            // 更新進度條
            TimeProgressBar.Value = _remainingSeconds;
            TotpTextBlock.Text = string.Format("還剩: {0}秒", _remainingSeconds);
            
        }

        private byte[] Base32Decode(string base32)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var output = new List<byte>();
            var bits = 0;
            var value = 0;

            foreach (var c in base32.ToUpperInvariant())
            {
                if (c == '=')
                    break;

                var index = alphabet.IndexOf(c);
                if (index < 0)
                    throw new FormatException("Invalid Base32 character");

                value = (value << 5) | index;
                bits += 5;

                if (bits >= 8)
                {
                    output.Add((byte)(value >> (bits - 8)));
                    bits -= 8;
                }
            }

            return output.ToArray();
        }

        public static long GetUnixTimeSeconds(int timeDeviation)
        {
            // 返回考慮時間偏差的 Unix 時間，使用 Ticks 屬性
            DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = DateTime.UtcNow - epochStart;
            return (long)timeSpan.TotalSeconds + timeDeviation;
        }

        private void OnlineCaliBT_Click(object sender, RoutedEventArgs e)
        {
            FetchNetworkTime();
        }

        private void plusBT_Click(object sender, RoutedEventArgs e)
        {
            // 更新偏差值顯示
            _timeDeviation=_timeDeviation+1;
            TimeDeviationAdj.Value = _timeDeviation;
            TimeDeviationValue.Text = _timeDeviation.ToString();

            // 將偏差值保存到儲存中
            IsolatedStorageSettings.ApplicationSettings["TimeDeviation"] = _timeDeviation;
            IsolatedStorageSettings.ApplicationSettings.Save();
        }

        private void MinusBT_Click(object sender, RoutedEventArgs e)
        {
            // 更新偏差值顯示
            _timeDeviation = _timeDeviation-1;
            TimeDeviationAdj.Value = _timeDeviation;
            TimeDeviationValue.Text = _timeDeviation.ToString();

            // 將偏差值保存到儲存中
            IsolatedStorageSettings.ApplicationSettings["TimeDeviation"] = _timeDeviation;
            IsolatedStorageSettings.ApplicationSettings.Save();
        }
    }
}
