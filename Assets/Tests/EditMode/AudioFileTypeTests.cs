using NUnit.Framework;
using UnityEngine;

namespace BabyDance.Tests
{
    public class AudioFileTypeTests
    {
        [TestCase("/a/b/song.mp3", AudioType.MPEG)]
        [TestCase("/a/b/SONG.MP3", AudioType.MPEG)]
        [TestCase("C:\\music\\loop.wav", AudioType.WAV)]
        [TestCase("/x/y.ogg", AudioType.OGGVORBIS)]
        [TestCase("/x/y.flac", AudioType.UNKNOWN)]
        [TestCase("/x/noext", AudioType.UNKNOWN)]
        public void MapsExtensionToAudioType(string path, AudioType expected)
        {
            Assert.That(AudioFileType.FromPath(path), Is.EqualTo(expected));
        }
    }
}
