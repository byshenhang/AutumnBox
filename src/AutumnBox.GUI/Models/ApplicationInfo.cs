using AutumnBox.GUI.MVVM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutumnBox.GUI.Models
{
    internal class ApplicationInfo : ViewModelBase
    {
        private bool _isSelected;

        public string PackageName { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                RaisePropertyChanged();
            }
        }
    }

}
