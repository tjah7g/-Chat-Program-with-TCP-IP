using ChatClient.MVVM.Core;
using ChatClient.MVVM.Model;
using ChatClient.Net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ChatClient.MVVM.ViewModel
{
    class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<UserModel> Users { get; set; }
        public ObservableCollection<string> Messages { get; set; }

        public RelayCommand ConnectToServerCommand { get; set; }
        public RelayCommand SendMessageCommand { get; set; }
        public RelayCommand DisconnectFromServerCommand { get; set; }
        public RelayCommand OpenPrivateChatCommand { get; set; }

        private Server _server;
        private Dictionary<string, DateTime> _typingUsers = new Dictionary<string, DateTime>();
        private System.Threading.Timer _typingCheckTimer;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private string _username;
        public string Username
        {
            get => _username;
            set
            {
                _username = value;
                OnPropertyChanged(nameof(Username));
                OnPropertyChanged(nameof(CanConnect));
            }
        }

        private string _message;
        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
                OnPropertyChanged(nameof(CanSendMessage));

                // Trigger typing indicator
                if (!string.IsNullOrEmpty(value) && IsConnected)
                {
                    _server?.StartTypingIndicator();
                }
            }
        }

        private string _ipAddress = "127.0.0.1";
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                _ipAddress = value;
                OnPropertyChanged(nameof(IpAddress));
                OnPropertyChanged(nameof(CanConnect));
            }
        }

        private string _port = "7891";
        public string Port
        {
            get => _port;
            set
            {
                _port = value;
                OnPropertyChanged(nameof(Port));
                OnPropertyChanged(nameof(CanConnect));
            }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsNotConnected));
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanSendMessage));
            }
        }

        public bool IsNotConnected => !IsConnected;

        public bool CanConnect => !string.IsNullOrWhiteSpace(Username) &&
                                  !string.IsNullOrWhiteSpace(IpAddress) &&
                                  int.TryParse(Port, out _) &&
                                  !IsConnected;

        public bool CanSendMessage => !string.IsNullOrWhiteSpace(Message) && IsConnected;

        private string _connectionStatus = "Disconnected";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                OnPropertyChanged(nameof(ConnectionStatus));
            }
        }

        private UserModel _selectedUser;
        public UserModel SelectedUser
        {
            get => _selectedUser;
            set
            {
                _selectedUser = value;
                OnPropertyChanged(nameof(SelectedUser));
            }
        }

        private string _typingIndicator;
        public string TypingIndicator
        {
            get => _typingIndicator;
            set
            {
                _typingIndicator = value;
                OnPropertyChanged(nameof(TypingIndicator));
            }
        }

        private bool _isDarkMode;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                _isDarkMode = value;
                OnPropertyChanged(nameof(IsDarkMode));
                ApplyTheme(value);
            }
        }

        public MainViewModel()
        {
            Users = new ObservableCollection<UserModel>();
            Messages = new ObservableCollection<string>();
            _server = new Server();

            _server.MessageReceived += HandleProtocol;
            _server.ConnectionStatusChanged += (status) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConnectionStatus = status;
                    IsConnected = status == "Connected";
                });
            };
            _server.ErrorOccurred += (title, message) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Add($"[ERROR] {title}: {message}");
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                });
            };

            ConnectToServerCommand = new RelayCommand(async o =>
            {
                if (int.TryParse(Port, out int port))
                {
                    ConnectionStatus = "Connecting...";
                    var success = await _server.ConnectToServerAsync(Username, IpAddress, port);
                    if (!success)
                    {
                        ConnectionStatus = "Connection Failed";
                    }
                }
                else
                {
                    MessageBox.Show("Invalid port number", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }, o => CanConnect);

            SendMessageCommand = new RelayCommand(async o =>
            {
                if (!string.IsNullOrWhiteSpace(Message))
                {
                    await _server.SendMessageAsync(Message);
                    Message = string.Empty;
                }
            }, o => CanSendMessage);

            DisconnectFromServerCommand = new RelayCommand(async o =>
            {
                await _server.DisconnectAsync();
                Users.Clear();
                ConnectionStatus = "Disconnected";
            }, o => IsConnected);

            OpenPrivateChatCommand = new RelayCommand(o =>
            {
                if (SelectedUser != null && SelectedUser.UserName != Username)
                {
                    var input = Microsoft.VisualBasic.Interaction.InputBox(
                        $"Send private message to {SelectedUser.UserName}:",
                        "Private Message",
                        "");

                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        _ = _server.SendPrivateMessageAsync(SelectedUser.UserName, input);
                    }
                }
            });

            // Start typing indicator check timer
            _typingCheckTimer = new System.Threading.Timer(_ => CheckTypingUsers(), null, 1000, 1000);
        }

        private void HandleProtocol(Protocol protocol)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (protocol.Type)
                {
                    case "join":
                        HandleUserJoin(protocol);
                        break;
                    case "leave":
                        HandleUserLeave(protocol);
                        break;
                    case "msg":
                        HandleMessage(protocol);
                        break;
                    case "pm":
                        HandlePrivateMessage(protocol);
                        break;
                    case "sys":
                        Messages.Add($"[SYSTEM] {protocol.Text}");
                        break;
                    case "typing":
                        HandleTyping(protocol);
                        break;
                    case "stoptyping":
                        HandleStopTyping(protocol);
                        break;
                }
            });
        }

        private void HandleUserJoin(Protocol protocol)
        {
            if (string.IsNullOrEmpty(protocol.From) || string.IsNullOrEmpty(protocol.UID))
            {
                // Invalid protocol, skip
                return;
            }

            var existingUser = Users.FirstOrDefault(u => u.UID == protocol.UID);
            if (existingUser == null)
            {
                Users.Add(new UserModel
                {
                    UserName = protocol.From,
                    UID = protocol.UID,
                    IsTyping = false
                });

                // Don't show notification for ourselves
                if (protocol.From != Username)
                {
                    Messages.Add($"[SYSTEM] {protocol.From} joined the chat");
                }
            }
        }

        private void HandleUserLeave(Protocol protocol)
        {
            var user = Users.FirstOrDefault(u => u.UID == protocol.UID);
            if (user != null)
            {
                Users.Remove(user);
                Messages.Add($"[SYSTEM] {protocol.From} left the chat");
            }
        }

        private void HandleMessage(Protocol protocol)
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(protocol.Timestamp).LocalDateTime;
            Messages.Add($"[{timestamp:HH:mm:ss}] {protocol.From}: {protocol.Text}");
        }

        private void HandlePrivateMessage(Protocol protocol)
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(protocol.Timestamp).LocalDateTime;

            if (protocol.From == Username)
            {
                Messages.Add($"[{timestamp:HH:mm:ss}] [PM to {protocol.To}]: {protocol.Text}");
            }
            else
            {
                Messages.Add($"[{timestamp:HH:mm:ss}] [PM from {protocol.From}]: {protocol.Text}");
            }
        }

        private void HandleTyping(Protocol protocol)
        {
            if (protocol.From != Username)
            {
                _typingUsers[protocol.From] = DateTime.Now;

                var user = Users.FirstOrDefault(u => u.UserName == protocol.From);
                if (user != null)
                {
                    user.IsTyping = true;
                }

                UpdateTypingIndicator();
            }
        }

        private void HandleStopTyping(Protocol protocol)
        {
            if (_typingUsers.ContainsKey(protocol.From))
            {
                _typingUsers.Remove(protocol.From);

                var user = Users.FirstOrDefault(u => u.UserName == protocol.From);
                if (user != null)
                {
                    user.IsTyping = false;
                }

                UpdateTypingIndicator();
            }
        }

        private void CheckTypingUsers()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var now = DateTime.Now;
                var expired = _typingUsers.Where(kvp => (now - kvp.Value).TotalSeconds > 3).ToList();

                foreach (var kvp in expired)
                {
                    _typingUsers.Remove(kvp.Key);
                    var user = Users.FirstOrDefault(u => u.UserName == kvp.Key);
                    if (user != null)
                    {
                        user.IsTyping = false;
                    }
                }

                if (expired.Any())
                {
                    UpdateTypingIndicator();
                }
            });
        }

        private void UpdateTypingIndicator()
        {
            if (_typingUsers.Count == 0)
            {
                TypingIndicator = "";
            }
            else if (_typingUsers.Count == 1)
            {
                TypingIndicator = $"{_typingUsers.Keys.First()} is typing...";
            }
            else
            {
                TypingIndicator = $"{_typingUsers.Count} users are typing...";
            }
        }

        private void ApplyTheme(bool isDark)
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null) return;

            var resources = mainWindow.Resources;

            if (isDark)
            {
                // Dark Theme
                UpdateResource(resources, "WindowBackground", Color.FromRgb(30, 30, 30));
                UpdateResource(resources, "PanelBackground", Color.FromRgb(45, 45, 45));
                UpdateResource(resources, "TextColor", Colors.White);
                UpdateResource(resources, "BorderColor", Color.FromRgb(60, 60, 60));
                UpdateResource(resources, "ButtonBackground", Color.FromRgb(0, 122, 204));
                UpdateResource(resources, "ButtonHover", Color.FromRgb(0, 90, 158));
            }
            else
            {
                // Light Theme
                UpdateResource(resources, "WindowBackground", Colors.White);
                UpdateResource(resources, "PanelBackground", Color.FromRgb(245, 245, 245));
                UpdateResource(resources, "TextColor", Colors.Black);
                UpdateResource(resources, "BorderColor", Color.FromRgb(204, 204, 204));
                UpdateResource(resources, "ButtonBackground", Color.FromRgb(0, 122, 204));
                UpdateResource(resources, "ButtonHover", Color.FromRgb(0, 90, 158));
            }
        }

        private void UpdateResource(ResourceDictionary resources, string key, Color color)
        {
            if (resources.Contains(key))
            {
                resources[key] = new SolidColorBrush(color);
            }
            else
            {
                resources.Add(key, new SolidColorBrush(color));
            }
        }
    }
}