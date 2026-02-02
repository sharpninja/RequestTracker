using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RequestTracker.Models.Json
{
    public partial class JsonTreeNode : ObservableObject
    {
        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _value;

        [ObservableProperty]
        private string _type;

        [ObservableProperty]
        private bool _isExpanded;

        public ObservableCollection<JsonTreeNode> Children { get; set; } = new();

        public JsonTreeNode(string name, string value, string type)
        {
            Name = name;
            Value = value;
            Type = type;
        }
    }
}
