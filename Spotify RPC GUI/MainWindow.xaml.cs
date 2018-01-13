using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SpotifyAPI.Local;
using SpotifyAPI.Local.Models;
using SpotifyAPI.Local.Enums;
using discordrpc;
using Hardcodet.Wpf.TaskbarNotification;
using Spotify_RPC_GUI.lib;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;
using Timer = System.Threading.Timer;

namespace Spotify_RPC_GUI {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();

            DiscordSpotify.LeftClickCommand = new CommandHandler(ShowAgain);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
            DiscordSpotify.Dispose();

            base.OnClosing(e);
        }

        private SpotifyLocalAPI _spotify;
        private bool _spotifyEnabled;

        private Timer _tickTimer;

        private string _smallImageKeyPlaying = "play";
        private string _smallImageTextPlaying = "Playing";
        private string _smallImageKeyPaused = "pause";
        private string _smallImageTextPaused = "Paused";

        private int _seconds = 10;

        private void Exit() {
            DiscordRpc.Shutdown();
        }

        private void Initialize() {
            _spotify = new SpotifyLocalAPI();

            var handlers = new DiscordRpc.EventHandlers {
                readyCallback = HandleReady,
                errorCallback = HandleError,
                disconnectedCallback = HandleDisconnected
            };

            DiscordRpc.Initialize("383020355553460224", ref handlers, true, null);

            DiscordRpc.RunCallbacks();

            SetStatus("Initializing");

            _tickTimer = new Timer(TimerTick, null, 1000, 1000);
        }

        private void Disconnect() {
            DiscordRpc.Shutdown();
            SetStatus("Disconnected");
            _tickTimer.Dispose();
            _tickTimer = null;
        }

        private void UpdateValues() {
            _smallImageKeyPlaying = smallKeyPlaying.Text;
            _smallImageTextPlaying = smallTextPlaying.Text;
            _smallImageKeyPaused = smallKeyPaused.Text;
            _smallImageTextPaused = smallTextPaused.Text;
        }

        private void SetStatus(string status) {
            Dispatcher.Invoke(() => {
                statusBox.Text = "Status: " + status;
            });
        }

        private void SetSong(string song) {
            Dispatcher.Invoke(() => {
                currentSongBox.Text = "Current Song: " + song;
            });
        }

        private void SetProgress(int progress) {
            Dispatcher.Invoke(() => {
                updateProgressBar.Value = progress;
                updateProgressBarText.Content = $"{progress}/15";
            });
        }

        private void HandleReady() {
            Console.WriteLine("Ready");
            SetStatus("Ready");
        }

        private void HandleDisconnected(int errorCode, string message) {
            Console.WriteLine("Disconnected");
            SetStatus("Disconnected");
            HandleError(errorCode, message);
        }

        private void HandleError(int errorCode, string message) {
            Console.WriteLine("Errored");
            SetStatus("Errored");
            MessageBox.Show(message, "Error: " + errorCode, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private long _pausedSince = 0;

        private void UpdatePresenceSpotify() {
            var presence = new DiscordRpc.RichPresence();
            var status = _spotify.GetStatus();

            Console.WriteLine($"Volume: {status.Volume}");

            var track = status.Track;

            if (status.Playing)
                presence.state = $"by {track.ArtistResource.Name}";
            else
                presence.state = "Paused VVV";

            presence.details = $"{track.TrackResource.Name}";

            presence.largeImageKey = "spotify";



            if (status.Shuffle && status.Repeat && status.NextEnabled)
                presence.largeImageText = "REPEAT & SHUFFLE";
            else if (status.Shuffle && status.Repeat && !status.NextEnabled)
                presence.largeImageText = "REPEAT(1) & SHUFFLE";
            else if (status.Shuffle && !status.Repeat && status.NextEnabled)
                presence.largeImageText = "SHUFFLE";
            else if (status.Shuffle && !status.Repeat && !status.NextEnabled)
                presence.largeImageText = "SHUFFLE (1?)";
            else if (!status.Shuffle && status.Repeat && status.NextEnabled)
                presence.largeImageText = "REPEAT";
            else if (!status.Shuffle && status.Repeat && !status.NextEnabled)
                presence.largeImageText = "REPEAT(1)";
            else
                presence.largeImageText = "Blank String here";

            var now = (long) (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            if (status.Playing) {
                if (_pausedSince != 0)
                    _pausedSince = 0;

                presence.startTimestamp = now - (long) status.PlayingPosition;
                presence.endTimestamp = presence.startTimestamp + track.Length;
                presence.smallImageKey = _smallImageKeyPlaying;
                presence.smallImageText = _smallImageTextPlaying;
            } else {
                if (_pausedSince == 0)
                    _pausedSince = now;

                presence.startTimestamp = _pausedSince;
                presence.smallImageKey = _smallImageKeyPaused;
                presence.smallImageText = _smallImageTextPaused;
            }

            SetSong(presence.details);

            DiscordRpc.UpdatePresence(ref presence);
        }

        private long _stoppedSince = 0;

        private void UpdatePresenceStopped() {
            var presence = new DiscordRpc.RichPresence {
                state = "Spotify Closed",
                largeImageKey = "spotify",
                largeImageText = "Spotify is closed...",
                smallImageKey = "stop",
                smallImageText = "Stopped"
            };

            presence.startTimestamp = _stoppedSince;
            DiscordRpc.UpdatePresence(ref presence);
        }

        private void TimerTick(object stateInfo) {
            if (!_spotifyEnabled && SpotifyLocalAPI.IsSpotifyRunning())
                if (_spotify.Connect()) {
                    _spotifyEnabled = true;
                    _stoppedSince = 0;
                    SetStatus("Connected");
                }


            if (_spotifyEnabled && !SpotifyLocalAPI.IsSpotifyRunning()) {
                _spotifyEnabled = false;
                if (_stoppedSince == 0)
                    _stoppedSince = (long) (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                SetStatus("Spotify Not Running");
            }


            _seconds++;

            if (_seconds == 15) {
                if (_spotifyEnabled)
                    UpdatePresenceSpotify();
                else
                    UpdatePresenceStopped();

                _seconds = 0;
            }

            try {
                Dispatcher.InvokeAsync(() => DiscordRpc.RunCallbacks());
            } catch (AccessViolationException e) {
                Console.WriteLine(e);
            }
            SetProgress(_seconds);
        }

        private void HandleInitialize(object sender, RoutedEventArgs e) {
            initButton.IsEnabled = false;
            disconnectButton.IsEnabled = true;
            Initialize();
        }

        private void HandleDisconnect(object sender, RoutedEventArgs e) {
            disconnectButton.IsEnabled = false;
            initButton.IsEnabled = true;
            Disconnect();
        }

        private void HandleUpdateValues(object sender, RoutedEventArgs e) {
            UpdateValues();
        }

        private ICommand _clickCommand;
        public ICommand ClickCommand => _clickCommand ?? (_clickCommand = new CommandHandler(ShowAgain));

        private void ShowAgain() {
            if (mainWindow.IsVisible)
                return;

            mainWindow.Show();
            WindowState = WindowState.Normal;
        }

        private void mainWindow_StateChanged(object sender, EventArgs e) {
            if (WindowState != WindowState.Minimized) return;

            mainWindow.Hide();
            DiscordSpotify.ToolTipText = "Click me to show the window again.";
        }

        private void HandleAbout(object sender, RoutedEventArgs e) {
            new About().Show();
        }

        private void HandleTutorial(object sender, RoutedEventArgs e) {
            MessageBox.Show("This does nothing yet.", "This does noting yet.", MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
        }
    }

    public class CommandHandler : ICommand {
        private Action _action;
        private bool _canExecute;

        public CommandHandler(Action action, bool canExecute = true) {
            _action = action;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) {
            return _canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public void Execute(object parameter) {
            _action();
        }
    }
}
