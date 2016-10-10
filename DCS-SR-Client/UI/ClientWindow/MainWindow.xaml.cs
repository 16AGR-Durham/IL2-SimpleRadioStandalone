﻿using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Overlay;
using MahApps.Metro.Controls;
using NAudio.Wave;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public delegate void ReceivedAutoConnect(string address, int port);

        public delegate void ToggleOverlayCallback(bool uiButton);

        private readonly AppConfiguration _appConfig;

        private readonly AudioManager _audioManager;

        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();

        private readonly string _guid;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private AudioPreview _audioPreview;
        private ClientSync _client;
        private DCSAutoConnectListener _dcsAutoConnectListener;
        private int _port = 5002;

        private RadioOverlayWindow _radioOverlayWindow;
        private AwacsRadioOverlayWindow.RadioOverlayWindow _awacsRadioOverlay;

        private IPAddress _resolvedIp;
        private ServerSettingsWindow _serverSettingsWindow;

        private bool _stop = true;

        //used to debounce toggle
        private double _toggleShowHide;

        private readonly DispatcherTimer _updateTimer;


        public MainWindow()
        {
            InitializeComponent();


            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            SetupLogging();

            Title = Title + " - " + UpdaterChecker.VERSION;

            Logger.Info("Started DCS-SimpleRadio Client " + UpdaterChecker.VERSION);

            InitInput();

            _appConfig = AppConfiguration.Instance;
            _guid = ShortGuid.NewGuid().ToString();

            InitAudioInput();

            InitAudioOutput();

            ServerIp.Text = _appConfig.LastServer;
            MicrophoneBoost.Value = _appConfig.MicBoost;
            SpeakerBoost.Value = _appConfig.SpeakerBoost;

            //   this.boostAmount.Content = "Boost: " + this.microphoneBoost.Value;
            _audioManager = new AudioManager(_clients);
            _audioManager.MicBoost = (float)MicrophoneBoost.Value;
            _audioManager.SpeakerBoost = (float)SpeakerBoost.Value;

            if ((BoostLabel != null) && (MicrophoneBoost != null))
            {
                BoostLabel.Content = (int)(MicrophoneBoost.Value * 100) - 100 + "%";
            }

            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = (int)(SpeakerBoost.Value * 100) - 100 + "%";
            }

            UpdaterChecker.CheckForUpdate();

            InitRadioSwitchIsPTT();

            InitRadioClickEffectsToggle();

            InitRadioClickEffectsTXToggle();

            InitRadioEncryptionEffectsToggle();

            InitAutoConnectPrompt();

            InitRadioOverlayTaskbarHide();

            InitRefocusDCS();

            _dcsAutoConnectListener = new DCSAutoConnectListener(AutoConnect);

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += UpdateClientCount_VUMeters;
            _updateTimer.Start();
        }

        private void InitInput()
        {
            InputManager = new InputDeviceManager(this, ToggleOverlay);

            Radio1.InputName = "Radio 1";
            Radio1.ControlInputBinding = InputBinding.Switch1;
            Radio1.InputDeviceManager = InputManager;

            Radio2.InputName = "Radio 2";
            Radio2.ControlInputBinding = InputBinding.Switch2;
            Radio2.InputDeviceManager = InputManager;

            Radio3.InputName = "Radio 3";
            Radio3.ControlInputBinding = InputBinding.Switch3;
            Radio3.InputDeviceManager = InputManager;

            PTT.InputName = "Common PTT";
            PTT.ControlInputBinding = InputBinding.Ptt;
            PTT.InputDeviceManager = InputManager;

            Intercom.InputName = "Intercom Select";
            Intercom.ControlInputBinding = InputBinding.Intercom;
            Intercom.InputDeviceManager = InputManager;

            RadioOverlay.InputName = "Overlay Toggle";
            RadioOverlay.ControlInputBinding = InputBinding.OverlayToggle;
            RadioOverlay.InputDeviceManager = InputManager;

            Radio4.InputName = "Radio 4";
            Radio4.ControlInputBinding = InputBinding.Switch4;
            Radio4.InputDeviceManager = InputManager;

            Radio5.InputName = "Radio 5";
            Radio5.ControlInputBinding = InputBinding.Switch5;
            Radio5.InputDeviceManager = InputManager;

            Radio6.InputName = "Radio 6";
            Radio6.ControlInputBinding = InputBinding.Switch6;
            Radio6.InputDeviceManager = InputManager;

            Radio7.InputName = "Radio 7";
            Radio7.ControlInputBinding = InputBinding.Switch7;
            Radio7.InputDeviceManager = InputManager;

            Radio8.InputName = "Radio 8";
            Radio8.ControlInputBinding = InputBinding.Switch8;
            Radio8.InputDeviceManager = InputManager;

            Radio9.InputName = "Radio 9";
            Radio9.ControlInputBinding = InputBinding.Switch9;
            Radio9.InputDeviceManager = InputManager;

            Radio10.InputName = "Radio 10";
            Radio10.ControlInputBinding = InputBinding.Switch10;
            Radio10.InputDeviceManager = InputManager;

        }

        public InputDeviceManager InputManager { get; set; }

        private void InitAudioInput()
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                Mic.Items.Add(WaveIn.GetCapabilities(i).ProductName);
            }

            if ((WaveIn.DeviceCount >= _appConfig.AudioInputDeviceId) && (WaveIn.DeviceCount > 0))
            {
                Mic.SelectedIndex = _appConfig.AudioInputDeviceId;
            }
            else if (WaveIn.DeviceCount > 0)
            {
                Mic.SelectedIndex = 0;
            }
        }

        private void UpdateClientCount_VUMeters(object sender, EventArgs e)
        {
            ClientCount.Content = _clients.Count;

            if (_audioPreview != null)
            {
                Mic_VU.Value = _audioPreview.MicMax;
                Speaker_VU.Value = _audioPreview.SpeakerMax;
            }
            else if (_audioManager != null)
            {
                Mic_VU.Value = _audioManager.MicMax;
                Speaker_VU.Value = _audioManager.SpeakerMax;
            }
        }

        private void InitAudioOutput()
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                Speakers.Items.Add(WaveOut.GetCapabilities(i).ProductName);
            }

            if ((WaveOut.DeviceCount >= _appConfig.AudioOutputDeviceId) && (WaveOut.DeviceCount > 0))
            {
                Speakers.SelectedIndex = _appConfig.AudioOutputDeviceId;
            }
            else if (WaveOut.DeviceCount > 0)
            {
                Speakers.SelectedIndex = 0;
            }
        }


        private void InitRadioClickEffectsToggle()
        {
            var radioEffects = Settings.Instance.UserSettings[(int)SettingType.RadioClickEffects];
            if (radioEffects == "ON")
            {
                RadioClicksToggle.IsChecked = true;
            }
            else
            {
                RadioClicksToggle.IsChecked = false;
            }
        }


        private void InitRadioClickEffectsTXToggle()
        {
            var radioEffects = Settings.Instance.UserSettings[(int)SettingType.RadioClickEffectsTx];
            if (radioEffects == "ON")
            {
                RadioClicksTXToggle.IsChecked = true;
            }
            else
            {
                RadioClicksTXToggle.IsChecked = false;
            }
        }

        private void InitRadioEncryptionEffectsToggle()
        {
            var radioEffects = Settings.Instance.UserSettings[(int)SettingType.RadioEncryptionEffects];
            if (radioEffects == "ON")
            {
                RadioEncryptionEffectsToggle.IsChecked = true;
            }
            else
            {
                RadioEncryptionEffectsToggle.IsChecked = false;
            }
        }

        private void InitRadioSwitchIsPTT()
        {
            var switchIsPTT = Settings.Instance.UserSettings[(int)SettingType.RadioSwitchIsPTT];
            if (switchIsPTT == "ON")
            {
                RadioSwitchIsPTT.IsChecked = true;
            }
            else
            {
                RadioSwitchIsPTT.IsChecked = false;
            }
        }

        private void InitAutoConnectPrompt()
        {
            var autoConnect = Settings.Instance.UserSettings[(int)SettingType.AutoConnectPrompt];
            if (autoConnect == "ON")
            {
                AutoConnectPromptToggle.IsChecked = true;
            }
            else
            {
                AutoConnectPromptToggle.IsChecked = false;
            }
        }

        private void InitRadioOverlayTaskbarHide()
        {
            var autoConnect = Settings.Instance.UserSettings[(int)SettingType.RadioOverlayTaskbarHide];
            if (autoConnect == "ON")
            {
                RadioOverlayTaskbarItem.IsChecked = true;
            }
            else
            {
                RadioOverlayTaskbarItem.IsChecked = false;
            }
        }

        private void InitRefocusDCS()
        {
            var refocus = Settings.Instance.UserSettings[(int)SettingType.RefocusDCS];
            if (refocus == "ON")
            {
                RefocusDCS.IsChecked = true;
            }
            else
            {
                RefocusDCS.IsChecked = false;
            }
        }



        private void SetupLogging()
        {
            // Step 1. Create configuration object 
            var config = new LoggingConfiguration();

            // Step 2. Create targets and add them to the configuration 
            var consoleTarget = new ColoredConsoleTarget();
            config.AddTarget("console", consoleTarget);

            var fileTarget = new FileTarget();
            config.AddTarget("file", fileTarget);

            // Step 3. Set target properties 
            consoleTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";
            fileTarget.FileName = "${basedir}/clientlog.txt";
            fileTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";

            // Step 4. Define rules
            //            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            //            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Info, fileTarget);
            config.LoggingRules.Add(rule2);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
        }

        private void startStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_stop)
            {
                Stop();
            }
            else
            {
                try
                {
                    //process hostname
                    var ipAddr = Dns.GetHostAddresses(GetAddressFromTextBox());

                    if (ipAddr.Length > 0)
                    {
                        _resolvedIp = ipAddr[0];
                        _port = GetPortFromTextBox();

                        _client = new ClientSync(_clients, _guid);
                        _client.TryConnect(new IPEndPoint(_resolvedIp, _port), ConnectCallback);

                        StartStop.Content = "Connecting...";
                        StartStop.IsEnabled = false;
                        Mic.IsEnabled = false;
                        Speakers.IsEnabled = false;
                    }
                    else
                    {
                        //invalid ID
                        MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private string GetAddressFromTextBox()
        {
            var addr = ServerIp.Text.Trim();

            if (addr.Contains(":"))
            {
                return addr.Split(':')[0];
            }

            return addr;
        }

        private int GetPortFromTextBox()
        {
            var addr = ServerIp.Text.Trim();

            try
            {
                if (addr.Contains(":"))
                {
                    return int.Parse(addr.Split(':')[1]);
                }
            }
            catch (Exception ex)
            {
                //no valid port! remove it
                ServerIp.Text = GetAddressFromTextBox();
            }

            return 5002;
        }

        private void Stop()
        {
            StartStop.Content = "Connect";
            StartStop.IsEnabled = true;
            Mic.IsEnabled = true;
            Speakers.IsEnabled = true;
            try
            {
                _audioManager.StopEncoding();
            }
            catch (Exception ex)
            {
            }

            _stop = true;

            if (_client != null)
            {
                _client.Disconnect();
                _client = null;
            }
        }

        private void ConnectCallback(bool result)
        {
            if (result)
            {
                if (_stop)
                {
                    StartStop.Content = "Disconnect";
                    StartStop.IsEnabled = true;

                    //save app settings
                    _appConfig.LastServer = ServerIp.Text.Trim();
                    _appConfig.AudioInputDeviceId = Mic.SelectedIndex;
                    _appConfig.AudioOutputDeviceId = Speakers.SelectedIndex;

                    _audioManager.StartEncoding(Mic.SelectedIndex, Speakers.SelectedIndex, _guid, InputManager,
                        _resolvedIp, _port);
                    _stop = false;
                }
            }
            else
            {
                Stop();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            //stop timer
            _updateTimer.Stop();

            Stop();

            if (_audioPreview != null)
            {
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }

            _radioOverlayWindow?.Close();
            _radioOverlayWindow = null;

            _dcsAutoConnectListener.Stop();
            _dcsAutoConnectListener = null;
        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                _audioPreview = new AudioPreview();
                _audioPreview.StartPreview(Mic.SelectedIndex, Speakers.SelectedIndex);
                _audioPreview.SpeakerBoost = (float)SpeakerBoost.Value;
                _audioPreview.MicBoost = (float)MicrophoneBoost.Value;
                Preview.Content = "Stop Preview";
            }
            else
            {
                Preview.Content = "Audio Preview";
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }
        }

        private void MicrophoneBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPreview != null)
            {
                _audioPreview.MicBoost = (float)MicrophoneBoost.Value;
            }
            if (_audioManager != null)
            {
                _audioManager.MicBoost = (float)MicrophoneBoost.Value;
            }
            if (_appConfig != null)
            {
                _appConfig.MicBoost = (float)MicrophoneBoost.Value;
            }

            if ((BoostLabel != null) && (MicrophoneBoost != null))
            {
                BoostLabel.Content = (int)(MicrophoneBoost.Value * 100) - 100 + "%";
            }
        }

        private void SpeakerBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPreview != null)
            {
                _audioPreview.SpeakerBoost = (float)SpeakerBoost.Value;
            }
            if (_audioManager != null)
            {
                _audioManager.SpeakerBoost = (float)SpeakerBoost.Value;
            }
            if (_appConfig != null)
            {
                _appConfig.SpeakerBoost = (float)SpeakerBoost.Value;
            }

            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = (int)(SpeakerBoost.Value * 100) - 100 + "%";
            }
        }

        private void RadioClicks_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioClickEffects, (string)RadioClicksToggle.Content);
        }

        private void RadioClicksTX_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioClickEffectsTx, (string)RadioClicksTXToggle.Content);
        }

        private void RadioEncryptionEffects_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioEncryptionEffects,
                (string)RadioEncryptionEffectsToggle.Content);
        }

        private void RadioSwitchPTT_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioSwitchIsPTT, (string)RadioSwitchIsPTT.Content);
        }

        private void ShowOverlay_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true);
        }

        private void ToggleOverlay(bool uiButton)
        {
            //debounce show hide
            if ((Environment.TickCount - _toggleShowHide > 600) || uiButton)
            {
                _toggleShowHide = Environment.TickCount;
                if ((_radioOverlayWindow == null) || !_radioOverlayWindow.IsVisible ||
                    (_radioOverlayWindow.WindowState == WindowState.Minimized))
                {
                    //hide awacs panel
                    _awacsRadioOverlay?.Close();
                    _awacsRadioOverlay = null;

                    _radioOverlayWindow?.Close();

                    _radioOverlayWindow = new RadioOverlayWindow();
                    _radioOverlayWindow.ShowInTaskbar =
                        Settings.Instance.UserSettings[(int)SettingType.RadioOverlayTaskbarHide] != "ON";
                    _radioOverlayWindow.Show();


                }
                else
                {
                    _radioOverlayWindow?.Close();
                    _radioOverlayWindow = null;
                }
            }
        }

        private void ShowAwacsOverlay_OnClick(object sender, RoutedEventArgs e)
        {

            if ((_awacsRadioOverlay == null) || !_awacsRadioOverlay.IsVisible ||
                (_awacsRadioOverlay.WindowState == WindowState.Minimized))
            {
                //close normal overlay
                _radioOverlayWindow?.Close();
                _radioOverlayWindow = null;

                _awacsRadioOverlay?.Close();

                _awacsRadioOverlay = new AwacsRadioOverlayWindow.RadioOverlayWindow();
                _awacsRadioOverlay.ShowInTaskbar =
                    Settings.Instance.UserSettings[(int)SettingType.RadioOverlayTaskbarHide] != "ON";
                _awacsRadioOverlay.Show();
            }
            else
            {
                _awacsRadioOverlay?.Close();
                _awacsRadioOverlay = null;
            }

        }

        private void AutoConnect(string address, int port)
        {
            Logger.Info("Received AutoConnect " + address);

            if (StartStop.Content.ToString().ToLower() == "connect")
            {
                var autoConnect = Settings.Instance.UserSettings[(int)SettingType.AutoConnectPrompt];

                if (autoConnect == "ON")
                {
                    WindowHelper.BringProcessToFront(Process.GetCurrentProcess());

                    var result = MessageBox.Show(this,
                        $"Would you like to try to Auto-Connect to DCS-SRS @ {address}:{port}? ", "Auto Connect",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if ((result == MessageBoxResult.Yes) && (StartStop.Content.ToString().ToLower() == "connect"))
                    {
                        ServerIp.Text = address + ":" + port;
                        startStop_Click(null, null);
                    }
                }
                else
                {
                    ServerIp.Text = address + ":" + port;
                    startStop_Click(null, null);
                }
            }
        }

        private void ResetRadioWindow_Click(object sender, RoutedEventArgs e)
        {
            //close overlay
            _radioOverlayWindow?.Close();
            _radioOverlayWindow = null;

            AppConfiguration.Instance.RadioX = 100;
            AppConfiguration.Instance.RadioY = 100;

            AppConfiguration.Instance.RadioWidth = 122;
            AppConfiguration.Instance.RadioHeight = 270;

            AppConfiguration.Instance.RadioOpacity = 1.0;
        }

        private void ToggleServerSettings_OnClick(object sender, RoutedEventArgs e)
        {
            if ((_serverSettingsWindow == null) || !_serverSettingsWindow.IsVisible ||
                (_serverSettingsWindow.WindowState == WindowState.Minimized))
            {
                _serverSettingsWindow?.Close();

                _serverSettingsWindow = new ServerSettingsWindow();
                _serverSettingsWindow.ShowDialog();
            }
            else
            {
                _serverSettingsWindow?.Close();
                _serverSettingsWindow = null;
            }
        }

        private void AutoConnectPromptToggle_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.AutoConnectPrompt, (string)AutoConnectPromptToggle.Content);
        }

        private void RadioOverlayTaskbarItem_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioOverlayTaskbarHide, (string)RadioOverlayTaskbarItem.Content);
        }


        private void DCSRefocus_OnClick_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RefocusDCS, (string)RefocusDCS.Content);
        }
    }
}