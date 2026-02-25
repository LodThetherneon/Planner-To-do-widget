from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, date

from PyQt6.QtCore import Qt, pyqtSignal, QSize, QPointF, QRectF
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
                    color = QColor("#FFFFFF") # Fehér ceruza

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
            # 11px sized pencil roughly
            painter.save()
            painter.translate(cx, cy)
            painter.rotate(45) # Rotate 45 degrees
            w = 2.0
            h = 8.0
            # Body
            painter.drawRect(QRectF(-w/2, -h/2, w, h))
            # Tip
            painter.drawPolygon([
                QPointF(-w/2, -h/2),
                QPointF(w/2, -h/2),
                QPointF(0, -h/2 - 3)
            ])
            painter.restore()

        elif self.icon_type == "save":
            # Floppy disk icon
            w = 10
            h = 10
            painter.drawRoundedRect(QRectF(cx - w/2, cy - h/2, w, h), 1, 1)
            # Inner white part
            # painter.drawRect(QRectF(cx - w/4, cy - h/2, w/2, h/3))
            # Shutter
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
    update_requested = pyqtSignal(str, str, str) # id, title, due

    def __init__(self, task: TaskViewModel, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.task = task
        self.is_editing_title = False
        self.is_editing_date = False

        bg, border = _card_colors(task)

        self.setFrameShape(QFrame.Shape.StyledPanel)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setStyleSheet(
            f"QFrame {{ background-color:{bg}; border:1px solid {border}; border-radius:10px; }}"
            "QLabel { background: transparent; border:0px; }"
            "QLineEdit { background: #333333; color: #FFFFFF; border: 1px solid #555555; border-radius: 4px; selection-background-color: #55AAFF; }"
        )

        self.lbl_title = QLabel(task.title)
        self.lbl_title.setWordWrap(True)
        if task.status == "FOLYAMATBAN":
            self.lbl_title.setStyleSheet("color:#FFFFFF; font-weight:800; font-size:13px; background: transparent; border:0px;")
        else:
            self.lbl_title.setStyleSheet("color:#707070; font-weight:700; font-size:11px; background: transparent; border:0px;")
        
        # Title editor (hidden by default)
        self.edit_title = QLineEdit()
        self.edit_title.setText(task.title)
        self.edit_title.setVisible(False)
        self.edit_title.returnPressed.connect(self._on_title_save)

        self.btn_delete = MinimalButton("trash", icon_size=28)
        self.btn_delete.setToolTip("Törlés")
        
        top_row_widget = QWidget()
        top_row_widget.setStyleSheet("background: transparent; border:0px;")
        top = QHBoxLayout(top_row_widget)
        top.setContentsMargins(15, 14, 15, 6)
        top.setSpacing(10)
        
        # Add widgets to top row
        top.addWidget(self.lbl_title, 1)
        top.addWidget(self.edit_title, 1)
        
        if task.status != "FOLYAMATBAN":
            # Edit title button for completed tasks
            self.btn_edit_title = MinimalButton("edit", icon_size=20)
            self.btn_edit_title.setToolTip("Név szerkesztése")
            self.btn_edit_title.clicked.connect(self._on_title_edit_clicked)
            # Add to layout right after title (will align right if title is short, or end of line)
            # Since we want it at the end of the text, but QLabel takes full width in this layout...
            # The simple QHBoxLayout puts it to the right of the label rect.
            top.addWidget(self.btn_edit_title, 0, Qt.AlignmentFlag.AlignTop)

        top.addWidget(self.btn_delete, 0, Qt.AlignmentFlag.AlignTop)

        if task.status == "FOLYAMATBAN":
            self.btn_left = MinimalButton("check", icon_size=30)
            # self.btn_left.setToolTip("Kész")
            self.btn_left.clicked.connect(self._on_done)
        else:
            self.btn_left = MinimalButton("undo", icon_size=30)
            # self.btn_left.setToolTip("Visszaállítás")
            self.btn_left.clicked.connect(self._on_reopen)

        due_text = task.due if task.due else "-"
        if task.status == "FOLYAMATBAN":
            self.lbl_date = QLabel(f"{due_text}")
            self.lbl_date.setStyleSheet("font-size:13px; color:#FFFFFF; background: transparent; border:0px;")
        else:
            self.lbl_date = QLabel(f"KÉSZ · {due_text}")
            self.lbl_date.setStyleSheet("font-size:11px; color:#888888; background: transparent; border:0px;")
        
        # Date editor (hidden by default)
        self.edit_date = QLineEdit()
        self.edit_date.setPlaceholderText("YYYY-MM-DD")
        self.edit_date.setText(task.due if task.due != "Nincs határidő" else "")
        self.edit_date.setVisible(False)
        self.edit_date.setFixedWidth(100)
        self.edit_date.returnPressed.connect(self._on_date_save)

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
        
        if task.status != "FOLYAMATBAN":
            # Date edit button to the left of date (but right of undo button)
            self.btn_edit_date = MinimalButton("edit", icon_size=20)
            self.btn_edit_date.setToolTip("Dátum szerkesztése")
            self.btn_edit_date.clicked.connect(self._on_date_edit_clicked)
            bottom.addWidget(self.btn_edit_date, 0)

        bottom.addWidget(self.lbl_date, 0) # Align left
        bottom.addWidget(self.edit_date, 0)
        bottom.addStretch(1) # Push everything to left
        
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
    
    def _on_title_edit_clicked(self) -> None:
        if not self.is_editing_title:
            # Start editing
            self.is_editing_title = True
            self.lbl_title.setVisible(False)
            self.edit_title.setVisible(True)
            self.edit_title.setFocus()
            self.btn_edit_title.icon_type = "save"
            self.btn_edit_title.update()
        else:
            # Save
            self._on_title_save()

    def _on_title_save(self) -> None:
        if not self.is_editing_title:
            return
        new_title = self.edit_title.text().strip()
        if new_title and new_title != self.task.title:
             self.update_requested.emit(self.task.id, new_title, self.task.due)
        
        # Even if no change, exit edit mode (wait for refresh to revert if failed, 
        # but to be responsive we can revert UI now or wait. 
        # Better to wait for refresh but let's at least disable edit mode UI)
        self.is_editing_title = False
        self.lbl_title.setVisible(True)
        self.edit_title.setVisible(False)
        self.btn_edit_title.icon_type = "edit"
        self.btn_edit_title.update()

    def _on_date_edit_clicked(self) -> None:
        if not self.is_editing_date:
            # Start editing
            self.is_editing_date = True
            self.lbl_date.setVisible(False)
            self.edit_date.setVisible(True)
            self.edit_date.setFocus()
            self.btn_edit_date.icon_type = "save"
            self.btn_edit_date.update()
        else:
            # Save
            self._on_date_save()

    def _on_date_save(self) -> None:
        if not self.is_editing_date:
            return
        new_due = self.edit_date.text().strip()
        ok, _ = validate_ymd(new_due)
        if not ok:
             # Flash error?
             self.edit_date.setStyleSheet("border: 1px solid red;")
             return
        
        self.edit_date.setStyleSheet("background: #333333; color: #FFFFFF; border: 1px solid #555555; border-radius: 4px;")
        
        if new_due != self.task.due:
             self.update_requested.emit(self.task.id, self.task.title, new_due)

        self.is_editing_date = False
        self.lbl_date.setVisible(True)
        self.edit_date.setVisible(False)
        self.btn_edit_date.icon_type = "edit"
        self.btn_edit_date.update()
