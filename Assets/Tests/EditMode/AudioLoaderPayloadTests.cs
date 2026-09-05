using NUnit.Framework;

namespace BabyDance.Tests
{
    public class AudioLoaderPayloadTests
    {
        [Test]
        public void SplitsNameAndUrlOnFirstNewline()
        {
            var (name, url) = AudioLoader.ParseChosenFile("song.mp3\nblob:https://example.com/0a1b-2c3d");
            Assert.That(name, Is.EqualTo("song.mp3"));
            Assert.That(url, Is.EqualTo("blob:https://example.com/0a1b-2c3d"));
        }

        [Test]
        public void MissingNewlineYieldsEmptyUrl()
        {
            var (name, url) = AudioLoader.ParseChosenFile("song.mp3");
            Assert.That(name, Is.EqualTo("song.mp3"));
            Assert.That(url, Is.Empty);
        }
    }
}
