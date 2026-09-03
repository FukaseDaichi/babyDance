using System.IO;
using UnityEngine;

namespace BabyDance
{
    public static class AudioFileType
    {
        public static AudioType FromPath(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
