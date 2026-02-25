# qt_sound.py
from __future__ import annotations

import os
import sys
from pathlib import Path

from PyQt6.QtCore import QUrl
from PyQt6.QtMultimedia import QMediaPlayer, QAudioOutput

# GLOBÁLIS szótár: Ez a legfontosabb!
# Megakadályozza, hogy a Python letörölje a lejátszót, mielőtt a hang véget érne.
# A tuple (QMediaPlayer, QAudioOutput) - MINDKETTŐT el kell tárolni,
# mert ha az QAudioOutput garbage collectálódik, a lejátszó azonnal elnémul.
_PLAYERS: dict[str, tuple[QMediaPlayer, QAudioOutput]] = {}

def _find_sound_path(filename: str) -> str | None:
    if hasattr(sys, "_MEIPASS"):
        p = Path(sys._MEIPASS) / filename
        if p.exists(): return str(p)

    if getattr(sys, 'frozen', False):
        p = Path(sys.executable).parent / filename
        if p.exists(): return str(p)

    p = Path(__file__).parent / filename
    if p.exists(): return str(p)

    return None

def play_sound(filename: str) -> None:
    path = _find_sound_path(filename)
    if not path or not os.path.exists(path):
        return

    # Csak egyszer hozzuk létre a lejátszót fájlonként, utána újrahasznosítjuk.
    # QMediaPlayer nem szenved az aszinkron betöltési problémától mint a QSoundEffect,
    # és MP3, WAV, AAC formátumokat egyaránt kezel.
    if filename not in _PLAYERS:
        player = QMediaPlayer()
        audio_output = QAudioOutput()
        audio_output.setVolume(1.0)
        player.setAudioOutput(audio_output)
        player.setSource(QUrl.fromLocalFile(path))
        _PLAYERS[filename] = (player, audio_output)

    player, _ = _PLAYERS[filename]
    # Ha épp játsza, állítsuk vissza az elejére és indítsuk újra
    player.stop()
    player.setPosition(0)
    player.play()
