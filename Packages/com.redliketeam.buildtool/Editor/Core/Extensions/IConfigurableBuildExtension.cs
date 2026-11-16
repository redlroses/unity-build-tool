using System.Collections.Generic;
using BuildTool.Editor.Pipeline;

namespace BuildTool.Editor.Extensions
{
    /// <summary>
    /// Optional interface for extensions that wish to receive the build execution context
    /// and CLI-provided arguments prior to running pre- and post-processors, as well as
    /// expose available CLI parameters and descriptions at runtime.
    /// </summary>
    public interface IConfigurableBuildExtension
    {
        void Initialize(BuildContext context);

        IEnumerable<ExtensionCliParam> GetParameters();
    }
}

