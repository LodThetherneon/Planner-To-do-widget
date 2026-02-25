# main.py
from __future__ import annotations
import sys
import os

if hasattr(sys, '_MEIPASS'):
    os.environ['QT_PLUGIN_PATH'] = os.path.join(sys._MEIPASS, 'PyQt6', 'Qt6', 'plugins')
from PyQt6.QtWidgets import QApplication
from PyQt6.QtCore import Qt

from qt_app import TaskHudWindow

def main() -> int:
    
    if hasattr(Qt.HighDpiScaleFactorRoundingPolicy, 'PassThrough'):
        QApplication.setHighDpiScaleFactorRoundingPolicy(
            Qt.HighDpiScaleFactorRoundingPolicy.PassThrough
        )
    
    app = QApplication(sys.argv)

    w = TaskHudWindow()
    w.show()
    
    return app.exec()

if __name__ == "__main__":
    raise SystemExit(main())
