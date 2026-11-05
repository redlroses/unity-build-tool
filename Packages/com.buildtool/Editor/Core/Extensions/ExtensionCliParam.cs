using System;

namespace BuildTool.Editor.Extensions
{
    [Serializable]
    public class ExtensionCliParam
    {
        public string Key;
        public string Type;
        public string Description;
        public string DefaultValue;

        public ExtensionCliParam()
        {
        }

        public ExtensionCliParam(string key, string type, string description, string defaultValue = "")
        {
            Key = key;
            Type = type;
            Description = description;
            DefaultValue = defaultValue;
        }
    }
}
