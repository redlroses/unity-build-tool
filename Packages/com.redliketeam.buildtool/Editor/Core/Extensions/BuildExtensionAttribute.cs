using System;

namespace BuildTool.Editor.Extensions
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class BuildExtensionAttribute : Attribute
    {
        public BuildExtensionAttribute(int order = 0, string title = null)
        {
            Order = order;
            Title = title;
        }

        public int Order { get; }
        public string Title { get; }
    }
}

