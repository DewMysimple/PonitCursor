"""Compare synthetic AudioProbe PCM with Windows output loopback. Requires numpy.

Run only with other audio stopped; loopback can contain other apps' audio.
This checks Windows output delivery, not a speaker/headphone's acoustic response.
"""
import sys
import numpy as np

prefix = sys.argv[1]
source = np.fromfile(prefix + "-source.pcm", dtype="<i2").astype(float)
recording = np.fromfile(prefix + "-loopback.pcm", dtype="<i2").astype(float)
size = 1 << (len(source) + len(recording) - 1).bit_length()
cross = np.fft.irfft(np.fft.rfft(recording, size) * np.conj(np.fft.rfft(source, size)), size)
cross = cross[:len(recording) - len(source) + 1]
onset = np.flatnonzero(abs(source) > 32)[0]
for begin, end in [(0, 72000), (72000, len(cross))]:
    offset = begin + np.argmax(cross[begin:end])
    actual = recording[offset:offset + len(source)]
    full = np.corrcoef(actual, source)[0, 1]
    head = np.corrcoef(actual[onset:onset + 2400], source[onset:onset + 2400])[0, 1]
    gain = np.dot(actual, source) / np.dot(source, source)
    print(f"offset={offset / 24:.1f}ms full={full:.8f} first100ms={head:.8f} gain={gain:.4f}")
    if not (full > 0.98 and head > 0.98 and gain > 0.01):
        raise SystemExit("FAIL: missing/distorted onset or interfering audio; inspect capture.")
print("PASS: both first and repeated playback retain onset and full waveform.")
