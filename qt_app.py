from __future__ import annotations

import json
import os
from datetime import datetime

try:
    import keyboard  # type: ignore
    _KEYBOARD_OK = True
except Exception:
    keyboard = None  # type: ignore
    _KEYBOARD_OK = False

from PyQt6.QtCore import Qt, QTimer, QSize, QPropertyAnimation, QEasingCurve, QPoint, pyqtSignal
from PyQt6.QtWidgets import (
    QWidget, QFrame, QVBoxLayout, QHBoxLayout, QLabel, QPushButton, QLineEdit,
    QComboBox, QScrollArea, QApplication
)
from PyQt6.QtWidgets import QStyleOptionSlider
from PyQt6.QtGui import QMouseEvent
from PyQt6.QtWidgets import QStyle

import backend

from ui_config import (
    REFRESH_RATE_SECONDS, WINDOW_WIDTH, WINDOW_MAX_HEIGHT, WINDOW_MIN_HEIGHT,
    ANIM_DURATION_MS, ALWAYS_ON_TOP,
    STARTSOUND, COMPLETESOUND, REOPENSOUND, today_ymd
)
from qt_styles import APP_QSS
from qt_sound import play_sound
from qt_workers import start_fetch, start_action
from qt_widgets import TaskViewModel, validate_ymd, TaskCard, SeparatorLine, MinimalButton


BUSY_GUARD_MS = 60000
DEFAULTS_FILE = "planner_defaults.json"

GLOBAL_HOTKEY = "alt+w"


def _load_defaults() -> dict:
    if not os.path.exists(DEFAULTS_FILE):
        return {}
    try:
        with open(DEFAULTS_FILE, "r", encoding="utf-8") as f:
            return json.load(f) or {}
    except Exception:
        return {}


def _save_defaults(data: dict) -> None:
    try:
        with open(DEFAULTS_FILE, "w", encoding="utf-8") as f:
            json.dump(data or {}, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


class TaskHudWindow(QWidget):
    hotkey_pressed = pyqtSignal()

    def __init__(self) -> None:
        super().__init__()

        flags = Qt.WindowType.Tool | Qt.WindowType.FramelessWindowHint
        if ALWAYS_ON_TOP:
            flags |= Qt.WindowType.WindowStaysOnTopHint
        self.setWindowFlags(flags)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)

        self._drag_pos: QPoint | None = None
        self._drag_start_global: QPoint | None = None
        self._dragging = False
        self._expanded = False
        self._is_refreshing = False
        self._jobs = []

        self._startup_banner_active = True
        self._hotkey_banner_active = False  # ÚJ flag a hotkey bannerhez
        self._last_counts_active: int | None = None
        self._last_counts_expired: int | None = None
        self._pending_status_text: str | None = None
        self._pending_status_kind: str | None = None

        self._last_tasks: list[TaskViewModel] = []
        self._completed_page = 1

        self._correcting_size = False

        self._defaults = _load_defaults()
        self._plan_items: list[dict] = []
        self._plan_by_label: dict[str, dict] = {}
        self._buckets_by_plan: dict[str, list[dict]] = {}

        self._selected_plan_id: str | None = None
        self._selected_bucket_id: str | None = None

        self._busy_guard = QTimer(self)
        self._busy_guard.setSingleShot(True)
        self._busy_guard.timeout.connect(self._on_busy_timeout)

        self.card = QFrame(self)
        self.card.setObjectName("MainCard")

        self.root = QVBoxLayout(self)
        self.root.setContentsMargins(10, 8, 10, 8)
        self.root.addWidget(self.card)

        self.card_layout = QVBoxLayout(self.card)
        self.card_layout.setContentsMargins(0, 0, 0, 0)
        self.card_layout.setSpacing(0)

        self.header = _Header(self)
        self.card_layout.addWidget(self.header)

        self.header.installEventFilter(self)
        self.header.lbl.installEventFilter(self)
        self.header.btn_refresh.installEventFilter(self)
        self.header.btn_toggle.installEventFilter(self)
        self.header.btn_close.installEventFilter(self)

        self.content = QWidget(self.card)
        self.content_layout = QVBoxLayout(self.content)
        self.content_layout.setContentsMargins(12, 0, 12, 12)
        self.content_layout.setSpacing(10)

        self.login_row = QWidget()
        lr = QHBoxLayout(self.login_row)
        lr.setContentsMargins(0, 0, 0, 0)
        lr.setSpacing(10)
        self.btn_login = QPushButton("Bejelentkezés")
        self.btn_login.clicked.connect(self.start_login_mainthread)
        self.lbl_hint = QLabel("Üdvözöllek, v1.04")
        self.lbl_hint.setStyleSheet("color:#AAB3BB; font-size:11px;")
        lr.addWidget(self.btn_login, 0)
        lr.addWidget(self.lbl_hint, 1)

        self.add_toggle = QPushButton("+ Új feladat")
        self.add_panel = _AddTaskPanel()
        self.add_panel.setVisible(False)
        self.add_toggle.clicked.connect(self._toggle_add_panel)
        self.add_panel.add_clicked.connect(self._on_add_clicked)
        self.add_panel.plan_changed.connect(self._on_plan_changed)
        self.add_panel.bucket_changed.connect(self._on_bucket_changed)

        self.content_layout.addWidget(self.login_row)
        self.content_layout.addWidget(self.add_toggle)
        self.content_layout.addWidget(self.add_panel)

        self.scroll = QScrollArea()
        self.scroll.setWidgetResizable(True)
        self.scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOn)
        self.scroll.setFrameShape(QFrame.Shape.NoFrame)
        self.scroll.setAutoFillBackground(False)
        self.scroll.viewport().setAutoFillBackground(False)
        self.scroll.viewport().setStyleSheet("background: transparent;")
        self.scroll.setViewportMargins(0, 0, 10, 0)

        self.scroll_host = QWidget()
        self.scroll_host.setAutoFillBackground(False)
        self.scroll_host.setStyleSheet("background: transparent;")
        self.scroll_layout = QVBoxLayout(self.scroll_host)
        self.scroll_layout.setContentsMargins(0, 0, 0, 0)
        self.scroll_layout.setSpacing(8)
        self.scroll_layout.addStretch(1)
        self.scroll.setWidget(self.scroll_host)

        self.scroll.verticalScrollBar().installEventFilter(self)

        self.content_layout.addWidget(self.scroll)
        self.card_layout.addWidget(self.content)

        self.setStyleSheet(APP_QSS)

        self.resize(WINDOW_WIDTH, WINDOW_MIN_HEIGHT)
        self._move_to_top_right()

        self.anim = QPropertyAnimation(self, b"size")
        self.anim.setDuration(ANIM_DURATION_MS)
        self.anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self.anim.finished.connect(self._on_anim_finished)

        self.header.refresh_clicked.connect(lambda: self.start_refresh(skip_intro=False))
        self.header.toggle_clicked.connect(self.toggle_expand)
        self.header.close_clicked.connect(self._close_requested)

        self._apply_expanded_state(False, immediate=True)

        self.timer = QTimer(self)
        self.timer.setInterval(REFRESH_RATE_SECONDS * 1000)
        self.timer.timeout.connect(lambda: self.start_refresh(skip_intro=True))
        self.timer.start()

        QTimer.singleShot(0, self._show_startup_banner)

        self.start_refresh(skip_intro=False)
        QTimer.singleShot(300, self._load_plans_from_graph)

        self.hotkey_pressed.connect(self.bring_to_front)
        if _KEYBOARD_OK and keyboard is not None:
            try:
                keyboard.add_hotkey(GLOBAL_HOTKEY, lambda: self.hotkey_pressed.emit())
            except Exception as e:
                print(f"Hotkey hiba: {e}")
        else:
            print("Hotkey: 'keyboard' nincs telepítve")

    def bring_to_front(self) -> None:
        self.showNormal()
        self.show()

        if ALWAYS_ON_TOP:
            self.raise_()
            self.activateWindow()
            return

        try:
            self.setWindowFlag(Qt.WindowType.WindowStaysOnTopHint, True)
            self.show()
            self.raise_()
            self.activateWindow()
            QTimer.singleShot(250, self._restore_not_on_top)
        except Exception:
            self.raise_()
            self.activateWindow()

    def _restore_not_on_top(self) -> None:
        try:
            self.setWindowFlag(Qt.WindowType.WindowStaysOnTopHint, False)
            self.show()
        except Exception:
            pass

    def showEvent(self, event) -> None:
        super().showEvent(event)
        handle = self.windowHandle()
        if handle:
            try:
                handle.screenChanged.connect(self._on_screen_changed)
            except Exception:
                pass

    def _on_screen_changed(self, _screen=None) -> None:
        if self.anim.state() == QPropertyAnimation.State.Running:
            self.anim.stop()
        self._correcting_size = True
        self._apply_expanded_state(self._expanded, immediate=True)
        self._correcting_size = False

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        if self._correcting_size:
            return
        if self.anim.state() == QPropertyAnimation.State.Running:
            return
        expected_h = WINDOW_MAX_HEIGHT if self._expanded else WINDOW_MIN_HEIGHT
        if self.width() != WINDOW_WIDTH or self.height() != expected_h:
            self._correcting_size = True
            self.resize(WINDOW_WIDTH, expected_h)
            self._correcting_size = False

    def _on_anim_finished(self) -> None:
        expected_h = WINDOW_MAX_HEIGHT if self._expanded else WINDOW_MIN_HEIGHT
        self._correcting_size = True
        self.resize(WINDOW_WIDTH, expected_h)
        self._correcting_size = False

    def _show_startup_banner(self) -> None:
        if not self._startup_banner_active:
            return
        self.header.set_text(
            '<span style="font-weight:900;">Planner Widget</span> '
            '<span style="font-weight:900; color:#55AAFF;">✓</span>'
        )
        try:
            self.header.lbl.repaint()
        except Exception:
            pass
        
        # MÓDOSÍTÁS: A banner után indítjuk a hotkey kijelzést, nem az alapállapotot
        QTimer.singleShot(2000, self._show_hotkey_info)

    # ÚJ METÓDUS: Hotkey információ megjelenítése (2 másodpercre)
    def _show_hotkey_info(self) -> None:
        self._startup_banner_active = False
        self._hotkey_banner_active = True
        
        if _KEYBOARD_OK:
            self.header.set_status(f"Gyorsbillentyű: {GLOBAL_HOTKEY}", kind="info")
        else:
            self.header.set_status("Gyorsbillentyű funkció nem elérhető", kind="warn")
            
        QTimer.singleShot(2000, self._hide_hotkey_info)

    # ÚJ METÓDUS: Hotkey eltüntetése és az eredeti számlálók visszaállítása
    def _hide_hotkey_info(self) -> None:
        self._hotkey_banner_active = False
        
        if self._pending_status_text:
            self.header.set_status(self._pending_status_text, kind=self._pending_status_kind or "info")
            self._pending_status_text = None
            self._pending_status_kind = None
            return

        if self._last_counts_active is not None and self._last_counts_expired is not None:
            self.header.set_counts(active=self._last_counts_active, expired=self._last_counts_expired)

    def eventFilter(self, obj, event):
        is_header_area = (
            (obj is self.header)
            or (obj is self.header.lbl)
            or (obj is self.header.btn_refresh)
            or (obj is self.header.btn_toggle)
            or (obj is self.header.btn_close)
        )

        if is_header_area:
            if event.type() == event.Type.MouseButtonPress and event.button() == Qt.MouseButton.LeftButton:
                gp = event.globalPosition().toPoint()
                self._drag_start_global = gp
                self._drag_pos = gp - self.frameGeometry().topLeft()
                self._dragging = False
                return False

            if event.type() == event.Type.MouseMove and (event.buttons() & Qt.MouseButton.LeftButton):
                if self._drag_pos is None or self._drag_start_global is None:
                    return False

                gp = event.globalPosition().toPoint()

                if not self._dragging:
                    if (gp - self._drag_start_global).manhattanLength() < QApplication.startDragDistance():
                        return False
                    self._dragging = True

                self.move(gp - self._drag_pos)
                event.accept()
                return True

            if event.type() == event.Type.MouseButtonRelease and event.button() == Qt.MouseButton.LeftButton:
                self._drag_pos = None
                self._drag_start_global = None
                was_dragging = self._dragging
                self._dragging = False
                return True if was_dragging else False

        if obj is self.scroll.verticalScrollBar():
            if event.type() == event.Type.MouseButtonPress and event.button() == Qt.MouseButton.LeftButton:
                sb = self.scroll.verticalScrollBar()

                opt = QStyleOptionSlider()
                sb.initStyleOption(opt)

                handle = sb.style().subControlRect(
                    QStyle.ComplexControl.CC_ScrollBar,
                    opt,
                    QStyle.SubControl.SC_ScrollBarSlider,
                    sb,
                )

                if isinstance(event, QMouseEvent):
                    pos = event.position().toPoint()
                else:
                    pos = QPoint(int(getattr(event, "x", lambda: 0)()), int(getattr(event, "y", lambda: 0)()))

                if not handle.contains(pos):
                    event.accept()
                    return True
                return False

        return super().eventFilter(obj, event)

    def _close_requested(self) -> None:
        app = QApplication.instance()
        if app is not None:
            app.quit()
        else:
            self.close()

    def closeEvent(self, event) -> None:
        try:
            if _KEYBOARD_OK and keyboard is not None:
                keyboard.unhook_all()
        except Exception:
            pass

        try:
            self._busy_guard.stop()
            self.timer.stop()
        except Exception:
            pass
        event.accept()
        app = QApplication.instance()
        if app is not None:
            app.quit()

    def _move_to_top_right(self) -> None:
        scr = QApplication.primaryScreen()
        if not scr:
            return
        geo = scr.availableGeometry()
        margin = 12
        self.move(geo.x() + geo.width() - self.width() - margin, geo.y() + margin)

    def _apply_expanded_state(self, expanded: bool, immediate: bool = False) -> None:
        self._expanded = expanded
        self.content.setVisible(expanded)
        self.header.set_toggle_icon("▴" if expanded else "▾")

        target_h = WINDOW_MAX_HEIGHT if expanded else WINDOW_MIN_HEIGHT
        target = QSize(WINDOW_WIDTH, target_h)

        if immediate:
            if self.anim.state() == QPropertyAnimation.State.Running:
                self.anim.stop()
            self.resize(target)
            return

        if self.anim.state() == QPropertyAnimation.State.Running:
            self.anim.stop()
        self.anim.setStartValue(self.size())
        self.anim.setEndValue(target)
        self.anim.start()

    def toggle_expand(self) -> None:
        self._apply_expanded_state(not self._expanded, immediate=False)

    def _on_busy_timeout(self) -> None:
        self._is_refreshing = False
        self.header.set_busy(False)
        self.set_status_guarded("Timeout", kind="warn")

    def _toggle_add_panel(self) -> None:
        now = not self.add_panel.isVisible()
        self.add_panel.setVisible(now)
        self.add_toggle.setText("- Bezárás" if now else "+ Új feladat")

    def set_status_guarded(self, text: str, kind: str = "info") -> None:
        # MÓDOSÍTÁS: Ha a startup banner VAGY a hotkey banner aktív, akkor csak elrakjuk későbbre
        if self._startup_banner_active or self._hotkey_banner_active:
            self._pending_status_text = text
            self._pending_status_kind = kind
            return
        self.header.set_status(text, kind=kind)

    def start_login_mainthread(self) -> None:
        self.btn_login.setDisabled(True)
        self.header.set_busy(True)
        self.set_status_guarded("Bejelentkezés...", kind="info")
        QApplication.processEvents()

        tok = None
        try:
            tok = backend.get_access_token_interactive()
        except Exception as e:
            self.set_status_guarded(f"Token hiba: {e}", kind="error")

        self.header.set_busy(False)
        self.btn_login.setDisabled(False)

        if not tok:
            self.set_status_guarded("Nem sikerült bejelentkezni.", kind="warn")
            return

        self.set_status_guarded("Planner Widget  ✓", kind="ok")
        self._load_plans_from_graph()
        QTimer.singleShot(450, lambda: self.start_refresh(skip_intro=True))

    def _load_plans_from_graph(self) -> None:
        ok, plans_or_msg = backend.list_my_plans()
        if not ok:
            return

        self._plan_items = plans_or_msg or []
        self._plan_by_label = {}
        labels = []

        for p in self._plan_items:
            pid = str(p.get("id") or "")
            title = str(p.get("title") or "").strip() or "(Névtelen terv)"
            if not pid:
                continue
            label = title
            if label in self._plan_by_label:
                label = f"{title} ({pid[:6]}…)"
            self._plan_by_label[label] = {"id": pid, "title": title}
            labels.append(label)

        labels = sorted(labels, key=lambda s: s.lower())
        self.add_panel.set_plan_options(labels)

        display_name = backend.get_my_display_name()
        if display_name:
            self.lbl_hint.setText(f"Üdvözöllek {display_name}, v1.03")
        else:
            self.lbl_hint.setText("Üdvözöllek, v1.03")

    def _on_plan_changed(self, plan_label: str) -> None:
        plan = self._plan_by_label.get(plan_label)
        if not plan:
            self._selected_plan_id = None
            self._selected_bucket_id = None
            self.add_panel.set_bucket_options([], enabled=False)
            return

        plan_id = plan["id"]
        self._selected_plan_id = plan_id

        if plan_id not in self._buckets_by_plan:
            okb, buckets_or_msg = backend.list_buckets_for_plan(plan_id)
            if not okb:
                self._buckets_by_plan[plan_id] = []
            else:
                self._buckets_by_plan[plan_id] = buckets_or_msg or []

        buckets = self._buckets_by_plan.get(plan_id, [])
        if not buckets:
            self._selected_bucket_id = None
            self.add_panel.set_bucket_options([], enabled=False)
            self.set_status_guarded("Nincs bucket ebben a tervben.", kind="warn")
            return

        if len(buckets) == 1:
            only = buckets[0]
            self._selected_bucket_id = str(only.get("id") or "")
            self.add_panel.set_bucket_options([str(only.get("name") or "(Névtelen bucket)")], enabled=False)
            self.set_status_guarded("Egy alap státusz", kind="ok")
            return

        bucket_names = [str(b.get("name") or "(Névtelen bucket)") for b in buckets]
        self.add_panel.set_bucket_options(sorted(bucket_names, key=lambda s: s.lower()), enabled=True)

        saved_bucket_id = self._defaults.get(plan_id)
        if saved_bucket_id:
            name = None
            for b in buckets:
                if str(b.get("id") or "") == saved_bucket_id:
                    name = str(b.get("name") or "")
                    break
            if name:
                self.add_panel.select_bucket_name(name)
                self._selected_bucket_id = saved_bucket_id
                return

        self._selected_bucket_id = None

    def _on_bucket_changed(self, bucket_name: str) -> None:
        if not self._selected_plan_id:
            return
        plan_id = self._selected_plan_id
        buckets = self._buckets_by_plan.get(plan_id, [])
        if not buckets:
            return

        chosen_id = None
        for b in buckets:
            if str(b.get("name") or "") == bucket_name:
                chosen_id = str(b.get("id") or "")
                break

        if not chosen_id:
            self._selected_bucket_id = None
            return

        self._selected_bucket_id = chosen_id
        self._defaults[plan_id] = chosen_id
        _save_defaults(self._defaults)
        self.set_status_guarded("Alap státusz elmentve.", kind="ok")

    def _on_add_clicked(self, title: str, due: str) -> None:
        title = title.strip()
        due = due.strip()

        if not title:
            self.set_status_guarded("Hiányzó név", kind="error")
            self.add_panel.flash_error("title")
            return

        ok, err = validate_ymd(due)
        if not ok:
            self.set_status_guarded(err or "Hibás dátum", kind="error")
            self.add_panel.flash_error("due")
            return

        if not self._selected_plan_id:
            self.set_status_guarded("Válassz csoportot.", kind="warn")
            return

        plan_id = self._selected_plan_id
        buckets = self._buckets_by_plan.get(plan_id, [])

        if len(buckets) > 1 and not self._selected_bucket_id:
            self.set_status_guarded("Több bucket van: válassz alap státuszt.", kind="warn")
            return

        if len(buckets) == 1:
            bucket_id = str(buckets[0].get("id") or "")
        else:
            bucket_id = self._selected_bucket_id or ""

        if not bucket_id:
            self.set_status_guarded("Nincs státusz kiválasztva.", kind="warn")
            return

        due_arg = None if not due else due

        self.set_status_guarded("Létrehozás...", kind="info")
        job = start_action("create_task", (title, bucket_id, plan_id, due_arg), self._on_action_finished_create)
        self._jobs.append(job)

    def _on_action_finished_create(self, ok: bool, msg: str) -> None:
        if not ok:
            self.set_status_guarded(msg or "Létrehozás sikertelen", kind="error")
            return
        self.add_panel.clear_inputs()
        if self.add_panel.isVisible():
            self._toggle_add_panel()
        self.start_refresh(skip_intro=True)

    def start_refresh(self, skip_intro: bool = True) -> None:
        if self._is_refreshing:
            return
        self._is_refreshing = True
        self.header.set_busy(True)

        if not self._startup_banner_active and not self._hotkey_banner_active:
            self.header.set_text("Frissítés...")

        self._busy_guard.start(BUSY_GUARD_MS)

        job = start_fetch(lambda data: self._on_fetched(data, skip_intro))
        self._jobs.append(job)

    def _on_fetched(self, data, skip_intro: bool) -> None:
        try:
            if self._busy_guard.isActive():
                self._busy_guard.stop()
        except Exception:
            pass

        self._is_refreshing = False
        self.header.set_busy(False)

        if isinstance(data, dict) and "error" in data:
            err = str(data.get("error") or "")
            if "Nincs bejelentkezve" in err:
                self.set_status_guarded("Jelentkezz be a frissítéshez.", kind="warn")
            else:
                self.set_status_guarded(f"Hiba: {err}", kind="warn")
            return

        if not isinstance(data, list):
            self.set_status_guarded("Ismeretlen válasz", kind="warn")
            return

        tasks_vm: list[TaskViewModel] = []
        for t in data:
            tasks_vm.append(
                TaskViewModel(
                    id=str(t.get("id", "")),
                    title=str(t.get("title", "")),
                    status=str(t.get("status", "")),
                    due=str(t.get("date", "")),
                    priority=str(t.get("priority", "medium")),
                )
            )

        self._update_header_counts(tasks_vm)

        if not skip_intro:
            play_sound(STARTSOUND)

        self._render_tasks(tasks_vm)

    def _update_header_counts(self, tasks: list[TaskViewModel]) -> None:
        today = datetime.now().date()
        active = 0
        expired = 0
        for t in tasks:
            if t.status == "FOLYAMATBAN":
                active += 1
                try:
                    dued = datetime.strptime(t.due, "%Y-%m-%d").date() if t.due and t.due != "Nincs határidő" else None
                except Exception:
                    dued = None
                if dued and dued < today:
                    expired += 1

        self._last_counts_active = active
        self._last_counts_expired = expired

        # MÓDOSÍTÁS: Ne frissítse a szöveget, amíg a bannerek futnak
        if self._startup_banner_active or self._hotkey_banner_active:
            return

        self.header.set_counts(active=active, expired=expired)

    def _clear_task_widgets(self) -> None:
        while self.scroll_layout.count() > 1:
            item = self.scroll_layout.takeAt(0)
            w = item.widget()
            if w is not None:
                w.deleteLater()

    def _prev_page(self) -> None:
        if self._completed_page > 1:
            self._completed_page -= 1
            self._render_tasks(self._last_tasks)

    def _next_page(self) -> None:
        done_count = sum(1 for t in self._last_tasks if t.status == "KESZ")
        max_pages = max(1, (done_count + 11) // 12)
        if self._completed_page < max_pages:
            self._completed_page += 1
            self._render_tasks(self._last_tasks)

    def _render_tasks(self, tasks: list[TaskViewModel]) -> None:
        self._last_tasks = tasks
        self._clear_task_widgets()

        today = datetime.now().date()

        def sort_key(t: TaskViewModel):
            if t.due and t.due != "Nincs határidő":
                try:
                    dued = datetime.strptime(t.due, "%Y-%m-%d").date()
                    diff = (dued - today).days
                except Exception:
                    dued = None
                    diff = 9999
            else:
                dued = None
                diff = 9999

            if t.status == "FOLYAMATBAN":
                if diff < 0:
                    group = 0
                elif diff <= 7:
                    group = 1
                elif diff == 9999:
                    group = 3
                else:
                    group = 2

                diff_sort = diff if diff != 9999 else 999999
                date_sort = dued.toordinal() if dued else 999999999
                return (0, group, diff_sort, date_sort, t.title.lower())

            if diff == 9999:
                sort_val = 999999999
            else:
                sort_val = -diff

            return (1, sort_val, t.title.lower())

        tasks_sorted = sorted(tasks, key=sort_key)

        active_tasks = [t for t in tasks_sorted if t.status == "FOLYAMATBAN"]
        done_tasks = [t for t in tasks_sorted if t.status == "KESZ"]

        for t in active_tasks:
            card = TaskCard(t)
            card.done_clicked.connect(self._on_done)
            card.reopen_clicked.connect(self._on_reopen)
            card.delete_clicked.connect(self._on_delete)
            card.update_requested.connect(self._on_update_task)
            self.scroll_layout.insertWidget(self.scroll_layout.count() - 1, card)

        if done_tasks:
            max_pages = max(1, (len(done_tasks) + 11) // 12)
            if self._completed_page > max_pages:
                self._completed_page = max_pages
            elif self._completed_page < 1:
                self._completed_page = 1

            self.scroll_layout.insertWidget(self.scroll_layout.count() - 1, SeparatorLine())

            if max_pages > 1:
                pag_widget = QWidget()
                pag_widget.setStyleSheet("background: transparent; border: 0px;")
                pag_layout = QHBoxLayout(pag_widget)
                pag_layout.setContentsMargins(0, 10, 0, 10)
                pag_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

                btn_prev = MinimalButton("left", icon_size=28)
                btn_prev.clicked.connect(self._prev_page)
                if self._completed_page == 1:
                    btn_prev.setDisabled(True)

                lbl_page = QLabel(f"{self._completed_page} / {max_pages}")
                lbl_page.setStyleSheet("color: #FFFFFF; font-size: 14px; font-weight: bold; background: transparent;")
                lbl_page.setAlignment(Qt.AlignmentFlag.AlignCenter)

                btn_next = MinimalButton("right", icon_size=28)
                btn_next.clicked.connect(self._next_page)
                if self._completed_page == max_pages:
                    btn_next.setDisabled(True)

                pag_layout.addWidget(btn_prev)
                pag_layout.addSpacing(15)
                pag_layout.addWidget(lbl_page)
                pag_layout.addSpacing(15)
                pag_layout.addWidget(btn_next)

                self.scroll_layout.insertWidget(self.scroll_layout.count() - 1, pag_widget)

            start_idx = (self._completed_page - 1) * 12
            end_idx = start_idx + 12
            page_tasks = done_tasks[start_idx:end_idx]

            for t in page_tasks:
                card = TaskCard(t)
                card.done_clicked.connect(self._on_done)
                card.reopen_clicked.connect(self._on_reopen)
                card.delete_clicked.connect(self._on_delete)
                card.update_requested.connect(self._on_update_task)
                self.scroll_layout.insertWidget(self.scroll_layout.count() - 1, card)

    def _on_done(self, task_id: str, title: str) -> None:
        if not self._startup_banner_active and not self._hotkey_banner_active:
            self.header.set_text("Frissítés...")
        job = start_action(
            "complete_task", (task_id, title),
            lambda ok, msg: (play_sound(COMPLETESOUND), self.start_refresh(skip_intro=True))
            if ok else self.set_status_guarded(msg or "Sikertelen", kind="error")
        )
        self._jobs.append(job)

    def _on_reopen(self, task_id: str, title: str) -> None:
        if not self._startup_banner_active and not self._hotkey_banner_active:
            self.header.set_text("Frissítés...")
        job = start_action(
            "reopen_task", (task_id, title),
            lambda ok, msg: (play_sound(REOPENSOUND), self.start_refresh(skip_intro=True))
            if ok else self.set_status_guarded(msg or "Sikertelen", kind="error")
        )
        self._jobs.append(job)

    def _on_delete(self, task_id: str, title: str) -> None:
        if not self._startup_banner_active and not self._hotkey_banner_active:
            self.header.set_text("Törlés...")
        job = start_action(
            "delete_task", (task_id,),
            lambda ok, msg: self.start_refresh(skip_intro=True)
            if ok else self.set_status_guarded(msg or "Törlés sikertelen", kind="error")
        )
        self._jobs.append(job)
        
    def _on_update_task(self, task_id: str, title: str, due: str) -> None:
        if not self._startup_banner_active and not self._hotkey_banner_active:
            self.header.set_text("Mentés...")
        # Késleltetjük a frissítést 1 másodperccel, hogy a Planner API is szinkronba kerüljön a háttérben
        job = start_action(
            "update_task_details", (task_id, title, due),
            lambda ok, msg: QTimer.singleShot(1000, lambda: self.start_refresh(skip_intro=True))
            if ok else self.set_status_guarded(msg or "Mentés sikertelen", kind="error")
        )
        self._jobs.append(job)


class _Header(QWidget):
    refresh_clicked = pyqtSignal()
    toggle_clicked = pyqtSignal()
    close_clicked = pyqtSignal()

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setObjectName("HeaderBar")

        self.lbl = QLabel("Betöltés...")
        self.lbl.setTextFormat(Qt.TextFormat.RichText)

        self.btn_refresh = MinimalButton("refresh")
        self.btn_toggle = MinimalButton("down")
        self.btn_close = MinimalButton("close")
        self.btn_close.setObjectName("CloseBtn")

        self.btn_refresh.setToolTip("Frissítés")
        self.btn_toggle.setToolTip("Kinyit/bezár")
        self.btn_close.setToolTip("Bezárás")

        row = QHBoxLayout(self)
        row.setContentsMargins(14, 12, 10, 10)
        row.setSpacing(8)
        row.addWidget(self.lbl, 1)
        row.addWidget(self.btn_refresh, 0)
        row.addWidget(self.btn_toggle, 0)
        row.addWidget(self.btn_close, 0)

        self.btn_refresh.clicked.connect(self.refresh_clicked.emit)
        self.btn_toggle.clicked.connect(self.toggle_clicked.emit)
        self.btn_close.clicked.connect(self.close_clicked.emit)

    def set_counts(self, active: int, expired: int) -> None:
        active = int(active or 0)
        expired = int(expired or 0)

        if expired > 0:
            html = (
                f"{active} <b>Feladat</b> | "
                f"<span style='color:#FF5555; font-weight:800;'>{expired} Lejárt</span>"
            )
        else:
            html = f"{active} <b>Feladat</b>"

        self.lbl.setStyleSheet("")
        self.lbl.setText(html)

    def set_text(self, text: str) -> None:
        self.lbl.setStyleSheet("")
        self.lbl.setText(text)

    def set_toggle_icon(self, txt: str) -> None:
        self.btn_toggle.icon_type = "up" if txt == "▴" else "down"
        self.btn_toggle.update()

    def set_busy(self, busy: bool) -> None:
        self.btn_refresh.setDisabled(busy)

    def set_status(self, text: str, kind: str = "info") -> None:
        if kind == "error":
            self.lbl.setStyleSheet("color:#FF7B7B; font-weight:700;")
        elif kind == "warn":
            self.lbl.setStyleSheet("color:#FFB86B; font-weight:700;")
        elif kind == "ok":
            self.lbl.setStyleSheet("color:#9BE59B; font-weight:700;")
        else:
            self.lbl.setStyleSheet("color:#EAEAEA; font-weight:700;")
        self.lbl.setText(text)


class _AddTaskPanel(QFrame):
    add_clicked = pyqtSignal(str, str)
    plan_changed = pyqtSignal(str)
    bucket_changed = pyqtSignal(str)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setStyleSheet("QFrame { background:#282828; border:1px solid #444; border-radius:8px; }")

        self.cmb_plan = QComboBox()
        self.cmb_plan.addItem("— Terv betöltése… —")
        self.cmb_plan.currentTextChanged.connect(self.plan_changed.emit)

        self.cmb_bucket = QComboBox()
        self.cmb_bucket.addItem("— Státusz —")
        self.cmb_bucket.setEnabled(False)
        self.cmb_bucket.currentTextChanged.connect(self.bucket_changed.emit)

        self.ed_title = QLineEdit()
        self.ed_title.setPlaceholderText("Új feladat neve...")

        self.ed_due = QLineEdit()
        self.ed_due.setPlaceholderText("ÉÉÉÉ-HH-NN")
        self.ed_due.setText(today_ymd())

        self.btn_add = QPushButton("Hozzáadás +")

        lay = QVBoxLayout(self)
        lay.setContentsMargins(10, 10, 10, 10)
        lay.setSpacing(10)
        lay.addWidget(self.cmb_plan)
        lay.addWidget(self.cmb_bucket)
        lay.addWidget(self.ed_title)

        row = QHBoxLayout()
        row.setContentsMargins(0, 0, 0, 0)
        row.setSpacing(10)
        row.addWidget(self.ed_due, 0)
        row.addWidget(self.btn_add, 1)
        lay.addLayout(row)

        self.btn_add.clicked.connect(self._emit_add)
        self.ed_title.returnPressed.connect(self._emit_add)

    def set_plan_options(self, labels: list[str]) -> None:
        self.cmb_plan.blockSignals(True)
        self.cmb_plan.clear()
        if not labels:
            self.cmb_plan.addItem("— Nincs terv —")
        else:
            self.cmb_plan.addItem("— Válassz csoportot —")
            for l in labels:
                self.cmb_plan.addItem(l)
        self.cmb_plan.blockSignals(False)
        self.set_bucket_options([], enabled=False)

    def set_bucket_options(self, bucket_names: list[str], enabled: bool) -> None:
        self.cmb_bucket.blockSignals(True)
        self.cmb_bucket.clear()
        if not bucket_names:
            self.cmb_bucket.addItem("— Státusz —")
        else:
            self.cmb_bucket.addItem("— Válassz alap státusz —")
            for n in bucket_names:
                self.cmb_bucket.addItem(n)
        self.cmb_bucket.setEnabled(bool(enabled))
        self.cmb_bucket.blockSignals(False)

    def select_bucket_name(self, name: str) -> None:
        idx = self.cmb_bucket.findText(name)
        if idx >= 0:
            self.cmb_bucket.setCurrentIndex(idx)

    def _emit_add(self) -> None:
        self.add_clicked.emit(self.ed_title.text(), self.ed_due.text())

    def clear_inputs(self) -> None:
        self.ed_title.clear()
        self.ed_due.setText(today_ymd())

    def flash_error(self, which: str) -> None:
        if which == "title":
            self.ed_title.setStyleSheet("border:1px solid #FF7B7B;")
            QTimer.singleShot(800, lambda: self.ed_title.setStyleSheet(""))
        elif which == "due":
            self.ed_due.setStyleSheet("border:1px solid #FF7B7B;")
            QTimer.singleShot(800, lambda: self.ed_due.setStyleSheet(""))
