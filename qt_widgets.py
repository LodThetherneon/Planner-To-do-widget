# qt_widgets.py
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, date

from PyQt6.QtCore import Qt, pyqtSignal, QSize, QPointF, QRectF
from PyQt6.QtGui import QPainter, QPen, QColor, QPainterPath, QFont
from PyQt6.QtWidgets import (
    QWidget, QFrame, QLabel, QHBoxLayout, QVBoxLayout, QPushButton,
    QSizePolicy
)


@dataclass(frozen=True)
class TaskViewModel:
    id: str
    title: str
    status: str
    due: str
    priority: str  # urgent/important/medium/low


def validate_ymd(d: str) -> tuple[bool, str | None]:
    if not d:
        return True, None
    if len(d) != 10 or d[4] != "-" or d[7] != "-":
        return False, "Hibás dátum formátum!"
    try:
        datetime.strptime(d, "%Y-%m-%d")
    except Exception:
        return False, "Hibás dátum formátum!"
    return True, None


def _parse_due(due: str) -> date | None:
    if not due or due == "Nincs határidő":
        return None
    try:
        return datetime.strptime(due, "%Y-%m-%d").date()
    except Exception:
        return None


def _card_colors(task: TaskViewModel) -> tuple[str, str]:
    if task.status != "FOLYAMATBAN":
        return ("#1A1A1A", "#2b2b2b")

    due = _parse_due(task.due)
    if not due:
        return ("#2b3542", "#404040")

    today = datetime.now().date()
    diff = (due - today).days
    if diff < 0:
        return ("#860000", "#6D0000")
    if diff <= 7:
        return ("#836900", "#554400")
    return ("#008300", "#005500")


def _prio_label_color(priority: str) -> tuple[str, str]:
    p = (priority or "medium").lower()
    if p == "urgent":
        return ("Sürgős", "#FF5555")
    if p == "important":
        return ("Fontos", "#FF5555")
    if p == "medium":
        return ("Közepes", "#FFB86B")
    return ("Alacsony", "#55AAFF")


class MinimalButton(QPushButton):
    def __init__(self, icon_type: str, icon_size: int = 28, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.icon_type = icon_type
        self.icon_size = icon_size
        self.setFixedSize(icon_size, icon_size)
        self.setCursor(Qt.CursorShape.PointingHandCursor)

    def paintEvent(self, event) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setRenderHint(QPainter.RenderHint.TextAntialiasing)

        bg_alpha = 0
        bg_color = QColor("#FFFFFF")
        color = QColor("#FFFFFF")

        if self.underMouse():
            bg_alpha = 20
            color = QColor("#FFFFFF")
            
            if self.icon_type in ["close", "trash"]:
                color = QColor("#FF5555")
                bg_color = QColor("#FF5555")
                bg_alpha = 30
            elif self.icon_type == "check":
                color = QColor("#00FF88")
                bg_color = QColor("#00FF88")
                bg_alpha = 20

        if not self.underMouse():
            if self.icon_type == "check":
                color = QColor("#FFFFFF")
            elif self.icon_type == "undo":
                color = QColor("#FFFFFF")
            elif self.icon_type == "trash":
                color = QColor("#E03F3F")

        if bg_alpha > 0:
            bg_color.setAlpha(bg_alpha)
            painter.setBrush(bg_color)
            painter.setPen(Qt.PenStyle.NoPen)
            painter.drawRoundedRect(self.rect(), 6, 6)

        pen = QPen(color)
        pen.setWidthF(1.8)
        pen.setCapStyle(Qt.PenCapStyle.RoundCap)
        pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
        painter.setPen(pen)
        painter.setBrush(Qt.BrushStyle.NoBrush)

        cx = self.width() / 2
        cy = self.height() / 2

        # Karakter alapú ikonok (refresh/undo) eltolás korrekcióval
        if self.icon_type in ["refresh", "undo"]:
            font = QFont("Segoe UI Symbol", 15)
            painter.setFont(font)
            # A korrekciót -1.5-ről -2.2-re növeltem, hogy még egy picit feljebb kerüljön
            text_rect = QRectF(self.rect()).translated(0, -2.2)
            symbol = "↻" if self.icon_type == "refresh" else "↺"
            painter.drawText(text_rect, Qt.AlignmentFlag.AlignCenter, symbol)

        elif self.icon_type == "close":
            d = self.width() * 0.16
            painter.drawLine(QPointF(cx - d, cy - d), QPointF(cx + d, cy + d))
            painter.drawLine(QPointF(cx + d, cy - d), QPointF(cx - d, cy + d))

        elif self.icon_type == "down":
            d = self.width() * 0.18
            painter.drawPolyline([
                QPointF(cx - d, cy - d/2),
                QPointF(cx, cy + d/2),
                QPointF(cx + d, cy - d/2)
            ])

        elif self.icon_type == "up":
            d = self.width() * 0.18
            painter.drawPolyline([
                QPointF(cx - d, cy + d/2),
                QPointF(cx, cy - d/2),
                QPointF(cx + d, cy + d/2)
            ])

        elif self.icon_type == "check":
            d = self.width() * 0.2
            painter.drawPolyline([
                QPointF(cx - d, cy),
                QPointF(cx - d/3, cy + d),
                QPointF(cx + d, cy - d + 1)
            ])

        elif self.icon_type == "trash":
            w = self.width() * 0.15
            h = self.height() * 0.22
            painter.drawPolyline([
                QPointF(cx - w, cy - h/2),
                QPointF(cx - w + 1, cy + h),
                QPointF(cx + w - 1, cy + h),
                QPointF(cx + w, cy - h/2)
            ])
            painter.drawLine(QPointF(cx - w - 2, cy - h/2), QPointF(cx + w + 2, cy - h/2))
            painter.drawLine(QPointF(cx - w/2, cy - h/2), QPointF(cx - w/2, cy - h/2 - 2))
            painter.drawLine(QPointF(cx + w/2, cy - h/2), QPointF(cx + w/2, cy - h/2 - 2))
            painter.drawLine(QPointF(cx - w/2, cy - h/2 - 2), QPointF(cx + w/2, cy - h/2 - 2))
            painter.drawLine(QPointF(cx - w/2 + 0.5, cy - h/2 + 3), QPointF(cx - w/2 + 1, cy + h - 2))
            painter.drawLine(QPointF(cx + w/2 - 0.5, cy - h/2 + 3), QPointF(cx + w/2 - 1, cy + h - 2))

        painter.end()

    def enterEvent(self, event) -> None:
        self.update()
        super().enterEvent(event)

    def leaveEvent(self, event) -> None:
        self.update()
        super().leaveEvent(event)


class SeparatorLine(QFrame):
    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setFixedHeight(2)
        self.setStyleSheet("QFrame { background:#444444; border:0px; border-radius:1px; }")


class TaskCard(QFrame):
    done_clicked = pyqtSignal(str, str)
    reopen_clicked = pyqtSignal(str, str)
    delete_clicked = pyqtSignal(str, str)

    def __init__(self, task: TaskViewModel, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.task = task

        bg, border = _card_colors(task)

        self.setFrameShape(QFrame.Shape.StyledPanel)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setStyleSheet(
            f"QFrame {{ background-color:{bg}; border:1px solid {border}; border-radius:10px; }}"
            "QLabel { background: transparent; border:0px; }"
        )

        self.lbl_title = QLabel(task.title)
        self.lbl_title.setWordWrap(True)
        if task.status == "FOLYAMATBAN":
            self.lbl_title.setStyleSheet("color:#FFFFFF; font-weight:800; font-size:15px; background: transparent; border:0px;")
        else:
            self.lbl_title.setStyleSheet("color:#707070; font-weight:700; font-size:11px; background: transparent; border:0px;")

        self.btn_delete = MinimalButton("trash", icon_size=28)
        self.btn_delete.setToolTip("Törlés")
        
        top_row_widget = QWidget()
        top_row_widget.setStyleSheet("background: transparent; border:0px;")
        top = QHBoxLayout(top_row_widget)
        top.setContentsMargins(15, 14, 15, 6)
        top.setSpacing(10)
        top.addWidget(self.lbl_title, 1)
        top.addWidget(self.btn_delete, 0, Qt.AlignmentFlag.AlignTop)

        if task.status == "FOLYAMATBAN":
            self.btn_left = MinimalButton("check", icon_size=30)
            self.btn_left.setToolTip("Kész")
            self.btn_left.clicked.connect(self._on_done)
        else:
            self.btn_left = MinimalButton("undo", icon_size=30)
            self.btn_left.setToolTip("Visszaállítás")
            self.btn_left.clicked.connect(self._on_reopen)

        due_text = task.due if task.due else "-"
        if task.status == "FOLYAMATBAN":
            self.lbl_date = QLabel(f"{due_text}")
            self.lbl_date.setStyleSheet("font-size:11px; color:#FFFFFF; background: transparent; border:0px;")
        else:
            self.lbl_date = QLabel(f"KÉSZ · {due_text}")
            self.lbl_date.setStyleSheet("font-size:11px; color:#888888; background: transparent; border:0px;")

        pr_txt, pr_col = _prio_label_color(task.priority)

        self.lbl_chip_dot = QLabel("●")
        self.lbl_chip_dot.setStyleSheet(f"color:{pr_col}; font-size:12px; background: transparent; border:0px;")
        self.lbl_chip_txt = QLabel(pr_txt)
        self.lbl_chip_txt.setStyleSheet(f"color:{pr_col}; font-size:11px; font-weight:700; background: transparent; border:0px;")

        chip = QWidget()
        chip.setStyleSheet("background: transparent; border:0px;")
        chip_l = QHBoxLayout(chip)
        chip_l.setContentsMargins(0, 0, 0, 0)
        chip_l.setSpacing(4)
        chip_l.addWidget(self.lbl_chip_dot, 0)
        chip_l.addWidget(self.lbl_chip_txt, 0)

        bottom_row_widget = QWidget()
        bottom_row_widget.setStyleSheet("background: transparent; border:0px;")
        bottom = QHBoxLayout(bottom_row_widget)
        bottom.setContentsMargins(15, 4, 15, 14)
        bottom.setSpacing(10)
        bottom.addWidget(self.btn_left, 0)
        bottom.addWidget(self.lbl_date, 1)
        if task.status == "FOLYAMATBAN":
            bottom.addWidget(chip, 0)

        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)
        root.addWidget(top_row_widget)
        root.addWidget(bottom_row_widget)

        self.btn_delete.clicked.connect(self._on_delete)

    def _on_done(self) -> None:
        self.done_clicked.emit(self.task.id, self.task.title)

    def _on_reopen(self) -> None:
        self.reopen_clicked.emit(self.task.id, self.task.title)

    def _on_delete(self) -> None:
        self.delete_clicked.emit(self.task.id, self.task.title)