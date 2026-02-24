# main.py
from __future__ import annotations
import sys
import os

from PyQt6.QtWidgets import QApplication
from PyQt6.QtCore import Qt

from qt_app import TaskHudWindow

def main() -> int:
    # 1. HiDPI beállítások (QApplication előtt kell legyenek!)
    # Ez megoldja, hogy ne vágja le az ablakot, ha más DPI-s monitorra húzod
    if hasattr(Qt.HighDpiScaleFactorRoundingPolicy, 'PassThrough'):
        QApplication.setHighDpiScaleFactorRoundingPolicy(
            Qt.HighDpiScaleFactorRoundingPolicy.PassThrough
        )
    
    # Opcionális: Ha nagyon szétcsúszik, próbáld meg ezt a környezeti változót is beállítani (kód elején):
    # os.environ["QT_AUTO_SCREEN_SCALE_FACTOR"] = "1"

    app = QApplication(sys.argv)

    w = TaskHudWindow()
    w.show()
    
    return app.exec()

if __name__ == "__main__":
    raise SystemExit(main())
