# qt_sound.py
from __future__ import annotations

import os
import sys
from pathlib import Path

from PyQt6.QtCore import QUrl
from PyQt6.QtMultimedia import QSoundEffect

# GLOBÁLIS szótár: Ez a legfontosabb! 
# Megakadályozza, hogy a Python letörölje a lejátszót, mielőtt a hang véget érne.
_PLAYERS: dict[str, QSoundEffect] = {}

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

    # Csak egyszer hozzuk létre a lejátszót fájlonként, utána újrahasznosítjuk
    if filename not in _PLAYERS:
        effect = QSoundEffect()
        # A QUrl.fromLocalFile tökéletesen kezeli az ékezetes (Széchenyi) útvonalakat
        effect.setSource(QUrl.fromLocalFile(path))
        effect.setVolume(1.0)
        _PLAYERS[filename] = effect

    # Ha épp játsza, állítsa le és indítsa újra
    _PLAYERS[filename].stop()
    _PLAYERS[filename].play()
