from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, date

from PyQt6.QtCore import Qt, pyqtSignal, QSize, QPointF, QRectF, QEvent
from PyQt6.QtGui import QPainter, QPen, QColor, QPainterPath, QFont
from PyQt6.QtWidgets import (
    QWidget, QFrame, QLabel, QHBoxLayout, QVBoxLayout, QPushButton,
    QSizePolicy, QLineEdit
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
        color = QColor("#AAB3BB")

        if not self.isEnabled():
            color = QColor("#555555")
        else:
            if self.underMouse():
                bg_alpha = 20
                color = QColor("#FFFFFF")
                
                if self.icon_type in ["close", "trash"]:
                    color = QColor("#FF5555")
                    bg_color = QColor("#FF5555")
                    bg_alpha = 30
                elif self.icon_type == "check" or self.icon_type == "save":
                    color = QColor("#00FF88")
                    bg_color = QColor("#00FF88")
                    bg_alpha = 20

            if not self.underMouse():
                if self.icon_type == "check" or self.icon_type == "save":
                    color = QColor("#FFFFFF")
                elif self.icon_type == "undo":
                    color = QColor("#55AAFF")
                elif self.icon_type == "trash":
                    color = QColor("#AA5555")
                elif self.icon_type == "edit":
                    color = QColor("#FFFFFF")

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

        if self.icon_type in ["refresh", "undo"]:
            font = QFont("Segoe UI Symbol", 15)
            painter.setFont(font)
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
            
        elif self.icon_type == "left":
            d = self.width() * 0.18
            painter.drawPolyline([
                QPointF(cx + d/2, cy - d),
                QPointF(cx - d/2, cy),
                QPointF(cx + d/2, cy + d)
            ])

        elif self.icon_type == "right":
            d = self.width() * 0.18
            painter.drawPolyline([
                QPointF(cx - d/2, cy - d),
                QPointF(cx + d/2, cy),
                QPointF(cx - d/2, cy + d)
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
        
        elif self.icon_type == "edit":
            painter.save()
            painter.translate(cx, cy)
            painter.rotate(45)
            w = 2.0
            h = 8.0
            painter.drawRect(QRectF(-w/2, -h/2, w, h))
            painter.drawPolygon([
                QPointF(-w/2, -h/2),
                QPointF(w/2, -h/2),
                QPointF(0, -h/2 - 3)
            ])
            painter.restore()

        elif self.icon_type == "save":
            w = 10
            h = 10
            painter.drawRoundedRect(QRectF(cx - w/2, cy - h/2, w, h), 1, 1)
            painter.drawRect(QRectF(cx - w/3, cy + h/6, w/1.5, h/3))

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
    content_changed = pyqtSignal()

    def __init__(self, task: TaskViewModel, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.task = task
        self.is_in_edit_mode = False
        self.original_title = task.title
        self.original_due = task.due if task.due != "Nincs határidő" else ""

        bg, border = _card_colors(task)

        self.setFrameShape(QFrame.Shape.StyledPanel)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setStyleSheet(
            f"QFrame {{ background-color:{bg}; border:1px solid {border}; border-radius:10px; }}"
            "QWidget#TransBg { background: transparent; border: 0px; }"
            "QLabel { background: transparent; border:0px; }"
            "QLineEdit { background: #333333; color: #FFFFFF; border: 1px solid #555555; border-radius: 4px; selection-background-color: #55AAFF; }"
        )

        self.lbl_title = QLabel(task.title)
        self.lbl_title.setWordWrap(True)
        if task.status == "FOLYAMATBAN":
            self.lbl_title.setStyleSheet("color:#FFFFFF; font-weight:800; font-size:13px; background: transparent; border:0px;")
        else:
            self.lbl_title.setStyleSheet("color:#707070; font-weight:700; font-size:11px; background: transparent; border:0px;")
        
        self.edit_title = QLineEdit()
        self.edit_title.setText(self.original_title)
        self.edit_title.setVisible(False)
        self.edit_title.installEventFilter(self)
        self.edit_title.textChanged.connect(self._on_text_changed)

        self.btn_delete = MinimalButton("trash", icon_size=28)
        self.btn_delete.setToolTip("Törlés")
        
        top_row_widget = QWidget()
        top_row_widget.setObjectName("TransBg")
        top = QHBoxLayout(top_row_widget)
        top.setContentsMargins(15, 14, 15, 6)
        top.setSpacing(10)
        
        top.addWidget(self.lbl_title, 1)
        top.addWidget(self.edit_title, 1)
        top.addWidget(self.btn_delete, 0, Qt.AlignmentFlag.AlignTop)

        if task.status == "FOLYAMATBAN":
            self.btn_left = MinimalButton("check", icon_size=30)
            self.btn_left.clicked.connect(self._on_done)
        else:
            self.btn_left = MinimalButton("undo", icon_size=30)
            self.btn_left.clicked.connect(self._on_reopen)

        due_text = task.due if task.due else "-"
        if task.status == "FOLYAMATBAN":
            self.lbl_date = QLabel(f"{due_text}")
            self.lbl_date.setStyleSheet("font-size:13px; color:#FFFFFF; background: transparent; border:0px;")
        else:
            self.lbl_date = QLabel(f"KÉSZ · {due_text}")
            self.lbl_date.setStyleSheet("font-size:11px; color:#888888; background: transparent; border:0px;")
        
        self.edit_date = QLineEdit()
        self.edit_date.setPlaceholderText("ÉÉÉÉ-HH-NN")
        self.edit_date.setText(self.original_due)
        self.edit_date.setVisible(False)
        self.edit_date.setFixedWidth(100)
        self.edit_date.installEventFilter(self)
        self.edit_date.textChanged.connect(self._on_text_changed)

        pr_txt, pr_col = _prio_label_color(task.priority)

        self.lbl_chip_dot = QLabel("●")
        self.lbl_chip_dot.setStyleSheet(f"color:{pr_col}; font-size:12px; background: transparent; border:0px;")
        self.lbl_chip_txt = QLabel(pr_txt)
        self.lbl_chip_txt.setStyleSheet(f"color:{pr_col}; font-size:11px; font-weight:700; background: transparent; border:0px;")

        chip = QWidget()
        chip.setObjectName("TransBg")
        chip_l = QHBoxLayout(chip)
        chip_l.setContentsMargins(0, 0, 0, 0)
        chip_l.setSpacing(4)
        chip_l.addWidget(self.lbl_chip_dot, 0)
        chip_l.addWidget(self.lbl_chip_txt, 0)

        bottom_row_widget = QWidget()
        bottom_row_widget.setObjectName("TransBg")
        bottom = QHBoxLayout(bottom_row_widget)
        bottom.setContentsMargins(15, 4, 15, 14)
        bottom.setSpacing(10)
        bottom.addWidget(self.btn_left, 0)
        bottom.addWidget(self.lbl_date, 0)
        bottom.addWidget(self.edit_date, 0)
        bottom.addStretch(1)
        
        if task.status == "FOLYAMATBAN":
            bottom.addWidget(chip, 0)

        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)
        root.addWidget(top_row_widget)
        root.addWidget(bottom_row_widget)

        self.btn_delete.clicked.connect(self._on_delete)

    def set_edit_mode(self, active: bool) -> None:
        self.is_in_edit_mode = active
        self.lbl_title.setVisible(not active)
        self.edit_title.setVisible(active)
        self.lbl_date.setVisible(not active)
        self.edit_date.setVisible(active)

        if not active:
            # Visszaállítás eredeti állapotra, a .setStyleSheet("") pedig törli az esetleges piros hibajelzést
            self.edit_title.setText(self.original_title)
            self.edit_title.setStyleSheet("")
            
            self.edit_date.setText(self.original_due)
            self.edit_date.setStyleSheet("")

    def has_changes(self) -> bool:
        t_val, d_val = self.get_changes()
        return t_val != self.original_title or d_val != self.original_due

    def get_changes(self) -> tuple[str, str]:
        current_t = self.edit_title.text().strip()
        current_d = self.edit_date.text().strip()
        current_d = current_d.replace(".", "-").replace("/", "-")
        if current_d.endswith("-"):
            current_d = current_d[:-1]
        return current_t, current_d

    def is_date_valid(self) -> bool:
        _, d_val = self.get_changes()
        ok, _ = validate_ymd(d_val)
        return ok

    def apply_changes_optimistic(self) -> None:
        t_val, d_val = self.get_changes()
        self.original_title = t_val
        self.original_due = d_val
        
        self.lbl_title.setText(t_val)
        self.lbl_date.setText(d_val if d_val else "Nincs határidő")

    def _on_text_changed(self) -> None:
        if self.is_in_edit_mode:
            self.content_changed.emit()

    def eventFilter(self, obj, event):
        if event.type() == QEvent.Type.KeyPress and event.key() == Qt.Key.Key_Escape:
            if obj is self.edit_title:
                self.edit_title.setText(self.original_title)
                return True
            elif obj is self.edit_date:
                self.edit_date.setText(self.original_due)
                return True
        return super().eventFilter(obj, event)

    def _on_done(self) -> None:
        self.done_clicked.emit(self.task.id, self.lbl_title.text())

    def _on_reopen(self) -> None:
        self.reopen_clicked.emit(self.task.id, self.lbl_title.text())

    def _on_delete(self) -> None:
        self.delete_clicked.emit(self.task.id, self.lbl_title.text())
