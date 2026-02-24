# main.py
from __future__ import annotations

import sys

from PyQt6.QtWidgets import QApplication

from qt_app import TaskHudWindow


def main() -> int:
    app = QApplication(sys.argv)

    w = TaskHudWindow()
    w.show()  # fontos: enélkül nem jelenik meg a widget [web:118][web:125]

    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
