namespace BlazorLinuxAdmin.Model
{
    using System.Collections.Generic;

    public class MyDirectory
    {
        public MyDirectory()
        {
            this.FullName = "";
            this.Name = "";
            this.Icon = "";
            this.Type = "";
            this.Children = new List<MyDirectory>();
        }

        public MyDirectory(string FullName, string Name, string Icon, string Type, List<MyDirectory> Children)
        {
            this.FullName = FullName;
            this.Name = Name;
            this.Icon = Icon;
            this.Type = Type;
            this.Children = Children;
        }

        public string FullName { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Type { get; set; }
        public List<MyDirectory> Children { get; set; }
    }
}
