import customtkinter as ctk
import backend
import threading
import ctypes
import os
import sys
import time
from datetime import datetime
import pygame

# toggle for a cleaner/minimalist test UI
# set to False to use original UI
CLEAN_STYLE = False

# --- ADATOK A LENYÍLÓ LISTÁHOZ ---
PLANNER_CONFIG = {
    "MFÜI PLANNER | Projektek": ("OYf_gOiqpEemTb0mmzHLhZYAE0Of", "08-2MpE1102uEKx-0cG_xpYAD_lu"),
    "Honlapos | To do":         ("2RF9b1ytzEqBJcA-sp2gzJYAMutW", "CVdS8ozNFkeBOc7eVX_XnZYAGwYA"),
    "BKR | Feladatok":          ("KT6NGsWmcUuBDusHUATs-5YAJdYx", "FjzlxFkDa0yNzX_FZRhzFJYAH4Iw"),
    "BKR | Folyamatban":        ("flrWWhF5tE2qRAGo1sR1qJYAHyq_", "FjzlxFkDa0yNzX_FZRhzFJYAH4Iw"),
    "MFÜI | Teendő":            ("rPHRYhH6Q0WHcszRL_3gI5YALKVI", "KBomUkFjOk2B7LN1ks_TZJYADClM"),
    "PLANNER | To do":          ("njefjFPVy0uopPImrwvdTZYAL3o-", "hjcsWBdGAkKzHKDa4qWdppYAGhtH"),
}

def lighten_color(hex_color, amount=25):
    try:
        if isinstance(hex_color, str) and hex_color.startswith('#') and len(hex_color) == 4:
            hex_color = '#' + ''.join(ch * 2 for ch in hex_color[1:])
        hex_color = hex_color.lstrip('#')
        rgb = tuple(int(hex_color[i:i+2], 16) for i in (0, 2, 4))
        new_rgb = tuple(min(255, c + amount) for c in rgb)
        return f'#{new_rgb[0]:02x}{new_rgb[1]:02x}{new_rgb[2]:02x}'
    except:
        return hex_color

def resource_path(relative_path):
    try:
        base_path = sys._MEIPASS
    except Exception:
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)

os.chdir(os.path.dirname(os.path.abspath(__file__)))

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(1)
except Exception:
    pass

REFRESH_RATE_SECONDS = 300
ANIMATION_SPEED_MS = 12
ANIMATION_SMOOTHNESS = 0.4

START_SOUND = resource_path("sound1.mp3")
COMPLETE_SOUND = resource_path("complete.mp3")
REOPEN_SOUND = resource_path("reopen.mp3")

FONT_TEXT_BOLD = ("Segoe UI", 12, "bold")
FONT_TEXT_NORMAL = ("Segoe UI", 12)
FONT_TEXT_SMALL = ("Segoe UI", 11)
FONT_ICON = ("Segoe MDL2 Assets", 14)

def play_sound(sound_file):
    try:
        if os.path.exists(sound_file):
            if not pygame.mixer.get_init():
                pygame.mixer.init()
            pygame.mixer.music.load(sound_file)
            pygame.mixer.music.set_volume(0.6)
            pygame.mixer.music.play()
    except Exception:
        pass

# Blur stub – nem okoz hibát, UI marad átlátszó
class WindowsBlur:
    def __init__(self, window):
        self.window = window
    def apply_blur(self):
        pass

class TaskWidget(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.last_fetched_tasks = []
        self.task_widgets = {}
        self.separator = None
        self.is_first_load = True

        self._date_cache = {}
        self.is_animating = False
        self.anim_job = None
        self.is_refreshing = False

        self.title("My ToDo HUD")
        self.overrideredirect(True)
        self.attributes('-topmost', False)

        self.transparent_color_key = "#000001"
        self.configure(fg_color=self.transparent_color_key)
        self.attributes("-transparentcolor", self.transparent_color_key)

        if os.name == 'nt':
            self.blur_manager = WindowsBlur(self)
            self.after(100, self.blur_manager.apply_blur)

        self.expanded = False
        self.window_width = 380
        self.max_height = 650
        self.min_height = 62

        screen_width = self.winfo_screenwidth()
        margin = 12
        x_pos = screen_width - self.window_width - margin
        y_pos = margin
        self.geometry(f"{self.window_width}x{self.min_height}+{x_pos}+{y_pos}")

        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)

        # HEADER
        self.header_frame = ctk.CTkFrame(
            self,
            corner_radius=10,
            fg_color=("#000000", "#2b3542"),
            bg_color=self.transparent_color_key,
        )
        self.header_frame.grid(row=0, column=0, sticky="ew", padx=10, pady=5)
        self.header_frame.bind("<Button-1>", self.start_move)
        self.header_frame.bind("<B1-Motion>", self.do_move)

        self.title_container = ctk.CTkFrame(self.header_frame, fg_color="transparent")
        self.title_container.pack(side="left", padx=15, pady=12)

        self.loading_label = ctk.CTkLabel(
            self.title_container,
            text="Betöltés...",
            font=FONT_TEXT_BOLD,
            text_color="#AAAAAA",
        )
        self.loading_label.pack(side="left")

        self.button_container = ctk.CTkFrame(self.header_frame, fg_color="transparent")
        self.button_container.pack(side="right", padx=(0, 10))

        btn_args = {
            "width": 36,
            "height": 30,
            "font": FONT_ICON,
            "fg_color": "transparent",
            "hover_color": "#444",
        }

        self.btn_refresh = ctk.CTkButton(
            self.button_container, text="\uE72C", command=self.start_refresh, **btn_args
        )
        self.btn_refresh.pack(side="left", padx=2)

        self.btn_toggle = ctk.CTkButton(
            self.button_container,
            text="\uE70D",
            command=self.toggle_size_animated,
            **btn_args,
        )
        self.btn_toggle.pack(side="left", padx=2)

        self.btn_close = ctk.CTkButton(
            self.button_container,
            text="\uE8BB",
            command=self.close_app,
            text_color="#AAAAAA",
            **btn_args,
        )
        self.btn_close.configure(hover_color="#CC5252")
        self.btn_close.pack(side="left", padx=2)

        # SCROLL FRAME
        self.scroll_frame = ctk.CTkScrollableFrame(
            self,
            corner_radius=10,
            fg_color=("#1a2428", "#2b3542"),
            bg_color=("#1a2428", "#2b3542"),
        )

        # ADD TASK UI
        self.expandable_add_task_container = ctk.CTkFrame(
            self.scroll_frame, fg_color="transparent"
        )
        self.expandable_add_task_container.pack(fill="x", padx=5, pady=5)

        self.is_add_section_open = False
        btn_bg = "#333333" if not CLEAN_STYLE else "transparent"
        hover = "#444444" if not CLEAN_STYLE else "#333"
        font_used = FONT_TEXT_BOLD if not CLEAN_STYLE else FONT_TEXT_NORMAL

        self.btn_toggle_add = ctk.CTkButton(
            self.expandable_add_task_container,
            text="+ Új feladat",
            font=font_used,
            height=30,
            fg_color=btn_bg,
            hover_color=hover,
            command=self.toggle_add_section,
        )
        self.btn_toggle_add.pack(fill="x")

        self.add_task_frame = ctk.CTkFrame(
            self.expandable_add_task_container,
            fg_color="#282828",
            corner_radius=8,
            border_width=1,
            border_color="#444",
        )

        self.bucket_var = ctk.StringVar(value=list(PLANNER_CONFIG.keys())[0])
        self.bucket_menu = ctk.CTkOptionMenu(
            self.add_task_frame,
            values=list(PLANNER_CONFIG.keys()),
            variable=self.bucket_var,
            font=FONT_TEXT_SMALL,
            height=32,
            corner_radius=6,
            fg_color="#1f1f1f",
            button_color="#1f1f1f",
            button_hover_color="#333333",
            dropdown_fg_color="#1f1f1f",
            dropdown_hover_color="#A16565",
            dropdown_text_color="white",
            dynamic_resizing=False,
            anchor="w",
        )
        self.bucket_menu.configure(
            fg_color="#3B535A",
            button_color="#1f1f1f",
            dropdown_fg_color="#2E2828",
            dropdown_hover_color="#3B535A",
        )
        self.bucket_menu.pack(fill="x", padx=10, pady=(10, 5))

        self.task_entry = ctk.CTkEntry(
            self.add_task_frame,
            placeholder_text="Új feladat neve...",
            font=FONT_TEXT_NORMAL,
            height=35,
        )
        self.task_entry.pack(fill="x", padx=10, pady=(0, 5))
        self.task_entry.bind("<Key>", lambda e: self._clear_name_error())
        self.task_entry.bind("<Return>", lambda e: self.add_new_task(e))

        entry_row = ctk.CTkFrame(self.add_task_frame, fg_color="transparent")
        entry_row.pack(fill="x", padx=10, pady=(0, 10))

        today_str = datetime.now().strftime("%Y-%m-%d")
        self.date_entry = ctk.CTkEntry(
            entry_row,
            placeholder_text="ÉÉÉÉ-HH-NN",
            font=FONT_TEXT_SMALL,
            height=35,
            width=120,
        )
        self.date_entry.insert(0, today_str)
        self.date_entry.pack(side="left", padx=(0, 5))

        self.btn_add = ctk.CTkButton(
            entry_row,
            text="Hozzáadás +",
            height=35,
            font=("Segoe UI", 13, "bold"),
            command=self.add_new_task,
            anchor="center",
            compound="right",
        )
        self.btn_add.pack(side="right", expand=True, fill="x")

        self.tasks_container = ctk.CTkFrame(self.scroll_frame, fg_color="transparent")
        self.tasks_container.pack(fill="x")
        self.tasks_container.grid_columnconfigure(0, weight=1)

        self.start_refresh()
        self.schedule_next_sync()

    def _parse_date(self, date_str):
        if not date_str or date_str == "Nincs határidő":
            return None
        if date_str in self._date_cache:
            return self._date_cache[date_str]
        try:
            parsed = datetime.strptime(date_str, "%Y-%m-%d").date()
            self._date_cache[date_str] = parsed
            return parsed
        except:
            return None

    def is_valid_date(self, date_string):
        if not date_string:
            return True, None
        if len(date_string) != 10 or date_string[4] != '-' or date_string[7] != '-':
            return False, "Hibás dátum formátum!"
        parts = date_string.split("-")
        try:
            year, month, day = map(int, parts)
        except Exception:
            return False, "Hibás dátum formátum!"
        if month < 1 or month > 12:
            return False, "Nem létező hónap"
        try:
            import calendar
            maxday = calendar.monthrange(year, month)[1]
            if day < 1 or day > maxday:
                return False, "Nem létező nap"
        except Exception:
            return False, "Hibás dátum formátum!"
        return True, None

    def toggle_add_section(self):
        if self.is_animating or self.btn_toggle_add.cget("state") == "disabled":
            return
        self.btn_toggle_add.configure(state="disabled")

        if self.is_add_section_open:
            self.add_task_frame.pack_forget()
            self.is_add_section_open = False
            self.btn_toggle_add.configure(text="+ Új feladat")
            self.task_entry.configure(border_color=["#979797", "#565b5e"])
            if isinstance(self.last_fetched_tasks, list):
                self._update_header_only(self.last_fetched_tasks)
            self.btn_toggle_add.configure(state="normal")
        else:
            self.add_task_frame.pack(fill="x", padx=5, pady=(5, 10))
            self.is_add_section_open = True
            self.btn_toggle_add.configure(text="- Bezárás")
            self.btn_toggle_add.configure(state="normal")

    def add_new_task(self, event=None):
        if self.btn_add.cget("state") == "disabled":
            return
        title = self.task_entry.get().strip()
        due_date = self.date_entry.get().strip()

        if not title:
            self.task_entry.configure(border_color="#FF7777")
            self.show_status_message("Hiányzó név", "#FF7777")
            return
        else:
            self.task_entry.configure(border_color=["#979797", "#565b5e"])

        if due_date:
            valid, err = self.is_valid_date(due_date)
            if not valid:
                self.date_entry.configure(border_color="#FF7777")
                self.show_status_message(err or "Hibás dátum formátum!", "#FF7777")
                return
            else:
                self.date_entry.configure(border_color=["#979797", "#565b5e"])

        if not due_date:
            due_date = None

        selected_key = self.bucket_var.get()
        bucket_id, plan_id = PLANNER_CONFIG[selected_key]

        self.btn_add.configure(state="disabled")
        self.show_status_message("Létrehozás...", "#FFFFFF")

        def worker():
            if backend.create_task(title, bucket_id, plan_id, due_date):
                self.after(0, lambda: self.task_entry.delete(0, 'end'))
                self.after(0, self.toggle_add_section)
                self.after(0, lambda: self.start_refresh(skip_intro=True))
                self.after(0, lambda: self.btn_add.configure(state="normal"))

        threading.Thread(target=worker, daemon=True).start()

    def start_move(self, event):
        self.x, self.y = event.x, event.y

    def do_move(self, event):
        x, y = self.winfo_x() + (event.x - self.x), self.winfo_y() + (event.y - self.y)
        self.geometry(f"+{x}+{y}")

    def close_app(self):
        self.destroy()
        sys.exit()

    def show_status_message(self, message, color="#AAAAAA"):
        for w in self.title_container.winfo_children():
            w.destroy()
        if message:
            ctk.CTkLabel(
                self.title_container, text=message, font=FONT_TEXT_BOLD, text_color=color
            ).pack(side="left")

    def _clear_status_message(self):
        for w in self.title_container.winfo_children():
            w.destroy()

    def _clear_name_error(self):
        try:
            self.task_entry.configure(border_color=["#979797", "#565b5e"])
        except Exception:
            pass
        self._clear_status_message()

    def mark_as_done(self, task_id, title):
        self.show_status_message("Frissítés...", "#FFFFFF")

        def worker():
            if backend.complete_task(task_id, title):
                play_sound(COMPLETE_SOUND)
                self.after(0, lambda: self.start_refresh(skip_intro=True))

        threading.Thread(target=worker, daemon=True).start()

    def delete_task(self, task_id, title=""):
        self.show_status_message("Törlés...", "#FF5555")
        if task_id in self.task_widgets:
            self.task_widgets[task_id].destroy()
            del self.task_widgets[task_id]

        def worker():
            ok = backend.delete_task(task_id)
            if not ok:
                self.after(
                    0,
                    lambda: self.show_status_message("Törlés sikertelen", "#FF7777"),
                )
            self.after(0, lambda: self.start_refresh(skip_intro=True))

        threading.Thread(target=worker, daemon=True).start()

    def undo_completion(self, task_id, title):
        self.show_status_message("Frissítés...", "#FFFFFF")

        def worker():
            if backend.reopen_task(task_id, title):
                play_sound(REOPEN_SOUND)
                self.after(0, lambda: self.start_refresh(skip_intro=True))

        threading.Thread(target=worker, daemon=True).start()

    def toggle_size_animated(self):
        # animáció nélkül, instant váltás
        if self.is_animating:
            return

        self.btn_toggle.configure(state="disabled")
        self.min_height = self.header_frame.winfo_height() + 10

        if self.expanded:
            self.btn_toggle.configure(text="\uE70D")
            self.expanded = False
            self.geometry(f"{self.window_width}x{int(self.min_height)}")
            self.scroll_frame.grid_forget()
            self.btn_toggle.configure(state="normal")
        else:
            self.btn_toggle.configure(text="\uE70E")
            self.expanded = True
            self.scroll_frame.grid(row=1, column=0, sticky="nsew", padx=10, pady=(0, 10))
            target_h = max(self.max_height, self.min_height + 200)
            self.geometry(f"{self.window_width}x{int(target_h)}")
            self.btn_toggle.configure(state="normal")

    def animate_height(self, current_h, target_h, on_complete=None):
        if self.anim_job is not None:
            try:
                self.after_cancel(self.anim_job)
            except Exception:
                pass
            self.anim_job = None

        self.is_animating = True
        diff = target_h - current_h

        if abs(diff) < 2:
            self.geometry(f"{self.window_width}x{int(target_h)}")
            self.is_animating = False
            if not self.expanded:
                self.scroll_frame.grid_forget()
            self.btn_toggle.configure(state="normal")
            if on_complete:
                on_complete()
            return

        step = diff * ANIMATION_SMOOTHNESS
        if abs(step) < 1:
            step = 1 if diff > 0 else -1
        new_h = int(current_h + step)
        self.geometry(f"{self.window_width}x{new_h}")
        self.anim_job = self.after(
            ANIMATION_SPEED_MS,
            lambda: self.animate_height(new_h, target_h, on_complete),
        )

    def _update_header_only(self, tasks):
        for w in self.title_container.winfo_children():
            w.destroy()
        active, expired, today = 0, 0, datetime.now().date()
        for t in tasks:
            if t['status'] == 'FOLYAMATBAN':
                active += 1
                date_obj = self._parse_date(t.get('date'))
                if date_obj and date_obj < today:
                    expired += 1
        if CLEAN_STYLE:
            ctk.CTkLabel(
                self.title_container,
                text=f"{active} feladat",
                font=FONT_TEXT_NORMAL,
                text_color="#EEE",
            ).pack(side="left", padx=5)
        else:
            ctk.CTkLabel(
                self.title_container,
                text=f"{active} Feladat",
                font=FONT_TEXT_BOLD,
                text_color="white",
            ).pack(side="left")
        if expired > 0:
            ctk.CTkLabel(
                self.title_container,
                text=" | ",
                font=FONT_TEXT_BOLD,
                text_color="gray",
            ).pack(side="left")
            ctk.CTkLabel(
                self.title_container,
                text=f"{expired} Lejárt",
                font=FONT_TEXT_BOLD,
                text_color="#FF7777",
            ).pack(side="left")

    def schedule_next_sync(self):
        self.after(REFRESH_RATE_SECONDS * 1000, self.run_auto_refresh)

    def run_auto_refresh(self):
        self.start_refresh(skip_intro=True)
        self.schedule_next_sync()

    def start_refresh(self, skip_intro=False):
        if self.btn_refresh.cget("state") == "disabled" or self.is_refreshing:
            return
        self.is_refreshing = True
        self.btn_refresh.configure(state="disabled")
        threading.Thread(
            target=self.fetch_and_update, args=(skip_intro,), daemon=True
        ).start()

    def fetch_and_update(self, skip_intro):
        data = backend.fetch_data()
        self.after(0, lambda: self.update_ui(data, skip_intro))

    def sort_tasks(self, task):
        date_str = task.get('date')
        today = datetime.now().date()
        due_date = self._parse_date(date_str)
        if not due_date:
            due_date = datetime(2099, 12, 31).date()
            diff = 9999
        else:
            diff = (due_date - today).days

        if task['status'] == 'FOLYAMATBAN':
            if diff < 0:
                return (0, due_date)
            if diff <= 7:
                return (1, due_date)
            if diff == 9999:
                return (3, due_date)
            return (2, due_date)

        abs_diff = abs(diff) if diff != 9999 else 9999
        return (4, abs_diff, -due_date.toordinal())

    def _tasks_equal(self, a, b):
        if len(a) != len(b):
            return False
        for x, y in zip(a, b):
            if (
                x.get('id') != y.get('id')
                or x.get('status') != y.get('status')
                or x.get('date') != y.get('date')
                or x.get('priority') != y.get('priority')
                or x.get('title') != y.get('title')
            ):
                return False
        return True

    def update_ui(self, tasks, skip_intro=False):
        self.btn_refresh.configure(state="normal")
        self.is_refreshing = False

        if isinstance(tasks, list) and not self.is_first_load and skip_intro:
            if self._tasks_equal(tasks, self.last_fetched_tasks):
                return

        if not isinstance(tasks, list):
            for w in self.title_container.winfo_children():
                w.destroy()
            if isinstance(tasks, dict) and "error" in tasks:
                ctk.CTkLabel(
                    self.title_container,
                    text=f"⚠️ {tasks['error']}",
                    font=FONT_TEXT_BOLD,
                    text_color="orange",
                ).pack(side="left")
            return

        self.last_fetched_tasks = tasks
        if self.is_first_load or not skip_intro:
            self.is_first_load = False
            self.show_intro_animation(tasks)
        else:
            self.render_task_status(tasks)

    def show_intro_animation(self, tasks):
        for w in self.title_container.winfo_children():
            w.destroy()
        intro_lbl = ctk.CTkLabel(
            self.title_container,
            text="Planner Widget ",
            font=FONT_TEXT_BOLD,
            text_color="#2b2b2b",
        )
        intro_lbl.pack(side="left")
        check_lbl = ctk.CTkLabel(
            self.title_container,
            text="\uE73E",
            font=("Segoe MDL2 Assets", 13, "bold"),
            text_color="#2b2b2b",
        )
        check_lbl.pack(side="left", padx=(0, 5))

        def fade(step):
            if step > 10:
                self.after(200, lambda: self.render_finished(tasks))
                return
            val = int(43 + (step * 21))
            color = f"#{val:02x}{val:02x}{val:02x}"
            intro_lbl.configure(text_color=color)
            if step > 5:
                check_lbl.configure(text_color="#00A2ED")
            self.after(15, lambda: fade(step + 1))

        fade(0)

    def render_finished(self, tasks):
        play_sound(START_SOUND)
        self.render_task_status(tasks)

    def render_task_status(self, tasks):
        for w in self.title_container.winfo_children():
            w.destroy()
        sorted_tasks = sorted(tasks, key=self.sort_tasks)

        active, expired, today = 0, 0, datetime.now().date()
        for t in sorted_tasks:
            if t['status'] == 'FOLYAMATBAN':
                active += 1
                date_obj = self._parse_date(t.get('date'))
                if date_obj and date_obj < today:
                    expired += 1

        if CLEAN_STYLE:
            ctk.CTkLabel(
                self.title_container,
                text=f"{active} feladat",
                font=FONT_TEXT_NORMAL,
                text_color="#EEE",
            ).pack(side="left", padx=5)
        else:
            ctk.CTkLabel(
                self.title_container,
                text=f"{active} Feladat",
                font=FONT_TEXT_BOLD,
                text_color="white",
            ).pack(side="left")
        if expired > 0:
            ctk.CTkLabel(
                self.title_container,
                text=" | ",
                font=FONT_TEXT_BOLD,
                text_color="gray",
            ).pack(side="left")
            ctk.CTkLabel(
                self.title_container,
                text=f"{expired} Lejárt",
                font=FONT_TEXT_BOLD,
                text_color="#FF7777",
            ).pack(side="left")

        current_ids = {t['id'] for t in sorted_tasks}
        for tid in list(self.task_widgets.keys()):
            if tid not in current_ids:
                self.task_widgets[tid].destroy()
                del self.task_widgets[tid]

        for w in self.task_widgets.values():
            w.grid_forget()
        if self.separator:
            self.separator.grid_forget()

        sep_placed = False
        _row_idx = 0

        for task in sorted_tasks:
            tid = task['id']
            if tid not in self.task_widgets:
                self.task_widgets[tid] = self.create_task_item_widget(task)
            else:
                self.update_widget_content(self.task_widgets[tid], task)

            if not sep_placed and task['status'] == "KESZ":
                if not self.separator:
                    self.separator = ctk.CTkFrame(
                        self.tasks_container, height=2, fg_color="#444444"
                    )
                self.separator.grid(row=_row_idx, column=0, sticky="ew", padx=10, pady=10)
                _row_idx += 1
                sep_placed = True

            self.task_widgets[tid].grid(
                in_=self.tasks_container,
                row=_row_idx,
                column=0,
                sticky="ew",
                pady=4,
                padx=2,
            )
            _row_idx += 1

    def update_widget_content(self, frame, task):
        if (
            getattr(frame, "current_status", None) == task.get("status")
            and getattr(frame, "current_title", None) == task.get("title")
            and getattr(frame, "current_date", None) == task.get("date")
            and getattr(frame, "current_priority", None) == task.get("priority")
        ):
            return
        for w in frame.winfo_children():
            w.destroy()
        self.build_task_card(frame, task)

    def create_task_item_widget(self, task):
        fr = ctk.CTkFrame(self.tasks_container, border_width=1, corner_radius=6)
        self.build_task_card(fr, task)
        return fr

    def build_task_card(self, fr, task):
        """Create a single task row. CLEAN_STYLE enables a very sparse look."""
        is_active = task['status'] == "FOLYAMATBAN"
        fr.current_status = task.get("status")
        fr.current_title = task.get("title")
        fr.current_date = task.get("date")
        fr.current_priority = task.get("priority")

        # SZÍNEK
        if CLEAN_STYLE:
            bg = "#636262"
            border = "#445255"
            title_col = "#EEE" if is_active else "#888"
        else:
            if is_active:
                bg = "#2b2b2b"
                border = "#404040"
                title_col = "white"
                date = task.get('date')
                if date and date != "Nincs határidő":
                    try:
                        diff = (
                            datetime.strptime(date, "%Y-%m-%d").date()
                            - datetime.now().date()
                        ).days
                        if diff < 0:
                            bg, border = "#860000", "#6D0000"
                        elif diff <= 7:
                            bg, border = "#836900", "#554400"
                        else:
                            bg, border = "#008300", "#005500"
                    except:
                        pass
            else:
                # KÉSZ: fix szürke
                bg = "#1A1A1A"
                border = "#2b2b2b"
                title_col = "#888888"

        fr.configure(
            fg_color=bg, border_color=border, border_width=0 if CLEAN_STYLE else 1
        )

        top_row = ctk.CTkFrame(fr, fg_color="transparent")
        top_row.pack(fill="x", padx=5 if CLEAN_STYLE else 10, pady=(5, 5))

        if CLEAN_STYLE:
            ctk.CTkLabel(
                top_row,
                text=task['title'],
                text_color=title_col,
                anchor="w",
                justify="left",
                wraplength=300,
                font=FONT_TEXT_NORMAL,
            ).pack(side="left", fill="x", expand=True, padx=4)
        else:
            if is_active:
                inner_box_bg = lighten_color(bg, amount=30)
                title_box = ctk.CTkFrame(
                    top_row,
                    fg_color=inner_box_bg,
                    border_width=1,
                    border_color=border,
                    corner_radius=5,
                )
                title_box.pack(side="left", fill="x", expand=True)
                ctk.CTkLabel(
                    title_box,
                    text=task['title'],
                    text_color=title_col,
                    anchor="w",
                    justify="left",
                    wraplength=300,
                    font=FONT_TEXT_BOLD,
                ).pack(padx=8, pady=4, fill="x")
            else:
                ctk.CTkLabel(
                    top_row,
                    text=task['title'],
                    text_color=title_col,
                    anchor="w",
                    justify="left",
                    wraplength=300,
                    font=FONT_TEXT_NORMAL,
                ).pack(side="left", fill="x", expand=True, padx=4)

        bot = ctk.CTkFrame(fr, fg_color="transparent")
        bot.pack(fill="x", padx=5 if CLEAN_STYLE else 10, pady=(0, 8))

        if CLEAN_STYLE:
            if is_active:
                ctk.CTkLabel(
                    bot, text=task.get('date',''), text_color="#AAA", font=FONT_TEXT_SMALL
                ).pack(side="left")
                ctk.CTkButton(
                    bot,
                    text="✔",
                    width=20,
                    height=20,
                    font=FONT_TEXT_SMALL,
                    fg_color="transparent",
                    hover_color="#333",
                    command=lambda tid=task['id'], ttitle=task['title']: self.mark_as_done(
                        tid, ttitle
                    ),
                ).pack(side="right")
            else:
                ctk.CTkLabel(
                    bot, text="✔", text_color="#555", font=FONT_TEXT_SMALL
                ).pack(side="left")
            return

        if is_active:
            ctk.CTkButton(
                bot,
                text="\uE73E",
                width=28,
                height=28,
                font=("Segoe MDL2 Assets", 12),
                fg_color="#333",
                hover_color="#00AA00",
                command=lambda tid=task['id'], ttitle=task['title']: self.mark_as_done(
                    tid, ttitle
                ),
            ).pack(side="left", padx=(0, 5))

            ctk.CTkLabel(
                bot,
                text=f"📅 {task.get('date','-')}",
                text_color="#AAAAAA",
                font=FONT_TEXT_SMALL
            ).pack(side="left", padx=5)

            p_map = {
                'high': ("Fontos", "#FF5555"),
                'low': ("Alacsony", "#55AAFF"),
                'medium': ("Közepes", "#AAAAAA"),
            }
            p_txt, p_col = p_map.get(task.get('priority'), ("Közepes", "#AAAAAA"))
            pc = ctk.CTkFrame(bot, fg_color="transparent", width=85, height=28)
            pc.pack(side="right")
            pc.pack_propagate(False)
            ctk.CTkLabel(
                pc, text="●", text_color=p_col, font=("Arial", 10), width=12
            ).pack(side="left")
            ctk.CTkLabel(
                pc, text=p_txt, text_color=p_col, font=("Segoe UI", 10, "bold")
            ).pack(side="left", padx=(2, 0))

            ctk.CTkButton(
                bot,
                text="\uE74D",
                width=28,
                height=28,
                font=("Segoe MDL2 Assets", 12),
                fg_color="transparent",
                hover_color="#A00",
                text_color="#FF5555",
                command=lambda tid=task['id'], ttitle=task['title']: self.delete_task(
                    tid, ttitle
                ),
            ).pack(side="right", padx=(4,0))
        else:
            chk = ctk.CTkFrame(
                bot,
                width=18,
                height=18,
                corner_radius=4,
                fg_color="transparent",
                border_width=1,
                border_color="#555",
            )
            chk.pack(side="left", padx=(0, 6))
            chk.pack_propagate(False)
            ctk.CTkLabel(
                chk, text="\uE73E", text_color="#00FF00", font=("Segoe MDL2 Assets", 10)
            ).place(relx=0.5, rely=0.5, anchor="center")

            ctk.CTkLabel(
                bot, text="KÉSZ", text_color="#888888", font=("Segoe UI", 10, "bold")
            ).pack(side="left")

            ctk.CTkLabel(
                bot, text=f"📅 {task.get('date','-')}", text_color="#888888", font=FONT_TEXT_SMALL
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                bot,
                text="\uE7A7",
                width=28,
                height=28,
                font=("Segoe MDL2 Assets", 12),
                fg_color="transparent",
                hover_color="#444",
                text_color="#888",
                command=lambda tid=task['id'], ttitle=task['title']: self.undo_completion(
                    tid, ttitle
                ),
            ).pack(side="right")

            ctk.CTkButton(
                bot,
                text="\uE74D",
                width=28,
                height=28,
                font=("Segoe MDL2 Assets", 12),
                fg_color="transparent",
                hover_color="#A00",
                text_color="#FF5555",
                command=lambda tid=task['id'], ttitle=task['title']: self.delete_task(
                    tid, ttitle
                ),
            ).pack(side="right", padx=(4,0))


if __name__ == "__main__":
    ctk.set_appearance_mode("Dark")
    app = TaskWidget()
    app.mainloop()
