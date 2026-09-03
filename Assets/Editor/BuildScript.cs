using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class BuildScript
    {
        private const string Tag = "[BabyDance]";

        /// <summary>CLI: tools/unity.sh build</summary>
        public static void BuildMac()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = "Builds/Mac/BabyDance.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"{Tag} BuildMac result={summary.result} size={summary.totalSize} errors={summary.totalErrors} path={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{Tag} build failed: {summary.result}");
            Debug.Log($"{Tag} BabyDance.Editor.BuildScript.BuildMac done");
        }
    }
}
