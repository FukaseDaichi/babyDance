using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class BuildScript
    {
        public const string OutputPath = "Builds/WebGL";

        /// <summary>CLI: tools/unity.sh build</summary>
        public static void BuildWebGL()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"{Log.Tag} BuildWebGL result={summary.result} size={summary.totalSize} errors={summary.totalErrors} path={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{Log.Tag} build failed: {summary.result}");
            Debug.Log($"{Log.Tag} BabyDance.Editor.BuildScript.BuildWebGL done");
        }
    }
}
