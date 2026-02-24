# qt_workers.py
from __future__ import annotations

import traceback
from PyQt6.QtCore import QObject, QRunnable, QThreadPool, pyqtSignal

import backend


class _Signals(QObject):
    finished = pyqtSignal(object)
    action_finished = pyqtSignal(bool, str)


class FetchRunnable(QRunnable):
    def __init__(self):
        super().__init__()
        self.signals = _Signals()

    def run(self) -> None:
        try:
            data = backend.fetch_data()
            self.signals.finished.emit(data)
        except Exception as e:
            self.signals.finished.emit({"error": f"{e}\n{traceback.format_exc()}"})


class ActionRunnable(QRunnable):
    def __init__(self, fn_name: str, args: tuple):
        super().__init__()
        self.fn_name = fn_name
        self.args = args
        self.signals = _Signals()

    def run(self) -> None:
        try:
            fn = getattr(backend, self.fn_name)
            res = fn(*self.args)

            # backend returns bool OR (bool, msg)
            if isinstance(res, tuple) and len(res) == 2:
                ok, msg = bool(res[0]), str(res[1] or "")
            else:
                ok, msg = bool(res), ""

            self.signals.action_finished.emit(ok, msg)
        except Exception as e:
            self.signals.action_finished.emit(False, f"{e}\n{traceback.format_exc()}")


def start_fetch(slot_finished):
    r = FetchRunnable()
    r.signals.finished.connect(slot_finished)
    QThreadPool.globalInstance().start(r)
    return r


def start_action(fn_name: str, args: tuple, slot_finished):
    r = ActionRunnable(fn_name, args)
    r.signals.action_finished.connect(slot_finished)
    QThreadPool.globalInstance().start(r)
    return r
