using System.ComponentModel;

namespace ChatClient.MVVM.Model
{
    class UserModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _userName;
        public string UserName
        {
            get => _userName;
            set
            {
                _userName = value;
                OnPropertyChanged(nameof(UserName));
            }
        }

        private string _uid;
        public string UID
        {
            get => _uid;
            set
            {
                _uid = value;
                OnPropertyChanged(nameof(UID));
            }
        }

        private bool _isTyping;
        public bool IsTyping
        {
            get => _isTyping;
            set
            {
                _isTyping = value;
                OnPropertyChanged(nameof(IsTyping));
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}