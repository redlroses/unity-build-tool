using System.Collections.Generic;
using UnityEditor.Build.Profile;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    /// <summary>
    /// Interface for custom build tool window extensions.
    /// Creates and returns a VisualElement representing the extension's UI.
    /// </summary>
    public interface IBuildWindowExtension
    {
        VisualElement CreateGUI(IEnumerable<BuildProfile> profiles);
    }
}

namespace BuildTool.Editor.Extensions
{
    /// <summary>
    /// Interface for custom build tool window extensions.
    /// </summary>
    public interface IBuildWindowExtension : global::BuildTool.Editor.IBuildWindowExtension
    {
    }
}

