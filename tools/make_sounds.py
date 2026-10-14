#!/usr/bin/env python3
"""Synthesises AVAZaxxon's sound effects and tunes into ../src/AVAZaxxon/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAZaxxon", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship's guns and what they hit.
    write("Fire.wav", mix(tone(0.12, 1800, 500, "square", release=1.2), tone(0.08, 3000, 900, "noise", release=1.5, gain=0.3)), 0.3)
    write("Hit.wav", mix(tone(0.35, 900, 120, "noise", release=0.9), tone(0.25, 220, 60, "square", release=0.8, gain=0.5)), 0.45)
    write("Boom.wav", mix(tone(0.8, 600, 60, "noise", release=0.7), tone(0.6, 140, 35, "sine", release=0.6, gain=0.8)), 0.55)
    write("BigBoom.wav", mix(tone(1.8, 400, 40, "noise", release=0.5), tone(1.5, 110, 25, "sine", release=0.5, gain=0.9), tone(1.2, 2000, 300, "noise", release=1.2, gain=0.3)), 0.6)
    write("PlayerBoom.wav", cat(mix(tone(0.3, 1200, 200, "noise", release=0.6), tone(0.3, 600, 100, "square", release=0.6, gain=0.4)), tone(1.4, 500, 40, "noise", release=0.6)), 0.6)
    write("Refuel.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6", "E6"]]), 0.4)
    write("Launch.wav", mix(tone(0.9, 150, 900, "noise", release=0.4), tone(0.9, 180, 700, "saw", release=0.6, gain=0.25)), 0.4)
    write("EnemyFire.wav", tone(0.18, 500, 200, "pulse", release=1.0), 0.3)
    write("Clank.wav", mix(tone(0.25, 1300, kind="tri", release=1.5), tone(0.25, 1870, kind="tri", release=1.8, gain=0.6), tone(0.05, 3000, 1000, "noise", release=1.0, gain=0.4)), 0.4)
    write("Warning.wav", cat(tone(0.12, 1250, kind="square", release=0.2), silence(0.05), tone(0.12, 1250, kind="square", release=0.2)), 0.3)
    write("Alarm.wav", cat(*[tone(0.16, f, f * 1.5, "square", release=0.2) for f in [500, 500, 500, 500]]), 0.35)
    write("Homing.wav", mix(tone(1.0, 300, 1400, "saw", release=0.3, vibrato=0.06, vib_rate=14), tone(1.0, 200, 600, "noise", release=0.5, gain=0.4)), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["G5", "C6", "E6", "G6", "C7", "G6", "C7"]]), 0.4)
    write("Section.wav", cat(tone(0.14, hz("E5"), kind="pulse", release=0.5), tone(0.14, hz("A5"), kind="pulse", release=0.5), tone(0.4, hz("E6"), kind="pulse", release=0.7)), 0.38)

    # Flying: a driving march in E minor over a pulsing bass (new). Loops.
    bass_notes = []
    for root in ["E2", "E2", "C3", "D3", "E2", "E2", "A2", "B2"] * 2:
        bass_notes += [(root, 0.5), (root, 0.5), (root, 0.5), (root, 0.5)]
    lead = [("E5", 1), ("G5", 0.5), ("B5", 0.5), ("A5", 1), ("G5", 1), ("E5", 1.5), ("D5", 0.5), ("E5", 2),
            ("C5", 1), ("E5", 0.5), ("G5", 0.5), ("F#5", 1), ("D5", 1), ("E5", 4),
            ("B4", 1), ("E5", 0.5), ("G5", 0.5), ("B5", 1), ("C6", 1), ("B5", 1.5), ("A5", 0.5), ("G5", 2),
            ("A5", 1), ("G5", 0.5), ("F#5", 0.5), ("E5", 1), ("D#5", 1), ("E5", 3), ("-", 1)]
    bass = seq(150, bass_notes, kind="tri", gain=0.5, release=0.6)
    top = seq(150, lead, kind="pulse", gain=0.26, release=0.5)
    write("Music.wav", mix(bass, top), 0.42)

    write("TitleTune.wav", tune(112,
                                [("E5", 1), ("B4", 0.5), ("E5", 0.5), ("G5", 1), ("F#5", 1), ("E5", 0.5), ("D5", 0.5), ("B4", 1), ("E5", 2),
                                 ("G5", 1), ("A5", 0.5), ("B5", 0.5), ("C6", 1), ("B5", 1), ("A5", 0.5), ("F#5", 0.5), ("D#5", 1), ("E5", 2)],
                                [("E3", 2), ("C3", 2), ("D3", 2), ("E3", 2), ("C3", 2), ("A2", 2), ("B2", 2), ("E3", 2)], kind="pulse"), 0.45)
    write("StartTune.wav", tune(160, [("E5", 0.5), ("E5", 0.5), ("B5", 1), ("G5", 0.5), ("A5", 0.5), ("B5", 2)], [("E3", 1), ("E3", 1), ("D3", 1), ("E3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(84, [("B5", 1), ("A5", 1), ("G5", 1), ("F#5", 1), ("E5", 3)], [("E4", 2), ("C4", 2), ("B3", 1), ("E3", 3)], kind="tri"), 0.45)
    write("LevelClear.wav", tune(150, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 1), ("B5", 0.5), ("E6", 0.5), ("G#6", 0.5), ("B6", 2), ("G#6", 0.5), ("E6", 2)],
                              [("E3", 1), ("B2", 1), ("E3", 1), ("G#3", 1), ("B3", 2), ("E3", 2)], kind="pulse"), 0.45)

if __name__ == "__main__":
    main()
