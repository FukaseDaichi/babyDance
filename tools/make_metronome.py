# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy"]
# ///
"""120 BPM のメトロノーム WAV（300 秒）を生成する。1 拍目は高い音、2〜4 拍目は低い音。"""
import wave
from pathlib import Path

import numpy as np

BPM = 120
SECONDS = 300
RATE = 44100
CLICK_SECONDS = 0.04


def click(freq: float) -> np.ndarray:
    t = np.arange(int(RATE * CLICK_SECONDS)) / RATE
    envelope = np.exp(-t * 80)
    return (np.sin(2 * np.pi * freq * t) * envelope).astype(np.float32)


def main() -> None:
    samples = np.zeros(RATE * SECONDS, dtype=np.float32)
    beat_len = 60.0 / BPM
    beat = 0
    while beat * beat_len < SECONDS:
        start = int(beat * beat_len * RATE)
        c = click(1760.0 if beat % 4 == 0 else 880.0)
        end = min(start + len(c), len(samples))
        samples[start:end] += c[: end - start]
        beat += 1
    pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2")
    out = Path(__file__).with_name(f"metronome_{BPM}bpm.wav")
    with wave.open(str(out), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print(out, len(pcm) / RATE, "sec")


if __name__ == "__main__":
    main()
