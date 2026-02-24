# ui_config.py
from __future__ import annotations
from datetime import datetime

WINDOW_WIDTH = 380
WINDOW_MAX_HEIGHT = 650
WINDOW_MIN_HEIGHT = 62

ANIM_DURATION_MS = 180
REFRESH_RATE_SECONDS = 300
ALWAYS_ON_TOP = False

STARTSOUND   = "sound1.wav"
COMPLETESOUND = "complete.wav"
REOPENSOUND  = "reopen.wav"



def today_ymd() -> str:
    return datetime.now().strftime("%Y-%m-%d")


# FONTOS: ugyanaz a sorrend mint a vidget.py-ben: (bucketId, planId). [file:1]
PLANNERCONFIG = {
    "MFI PLANNER Projektek": ("OYfgOiqpEemTb0mmzHLhZYAE0Of", "08-2MpE1102uEKx-0cGxpYADlu"),
    "Honlapos To do": ("2RF9b1ytzEqBJcA-sp2gzJYAMutW", "CVdS8ozNFkeBOc7eVXXnZYAGwYA"),
    "BKR Feladatok": ("KT6NGsWmcUuBDusHUATs-5YAJdYx", "FjzlxFkDa0yNzXFZRhzFJYAH4Iw"),
    "BKR Folyamatban": ("flrWWhF5tE2qRAGo1sR1qJYAHyq", "FjzlxFkDa0yNzXFZRhzFJYAH4Iw"),
    "MFI Teend": ("rPHRYhH6Q0WHcszRL3gI5YALKVI", "KBomUkFjOk2B7LN1ksTZJYADClM"),
    "PLANNER To do": ("njefjFPVy0uopPImrwvdTZYAL3o-", "hjcsWBdGAkKzHKDa4qWdppYAGhtH"),
}
