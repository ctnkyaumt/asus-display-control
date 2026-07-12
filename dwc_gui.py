import os
import sys
import json
import shutil
import subprocess
import threading
import time
import tkinter as tk
from tkinter import messagebox, filedialog
from tkinter import ttk

# ==============================================================================
# STYLE CONFIGURATION
# ==============================================================================
BG_SIDEBAR = "#0f172a"      # slate-900 (deep blue-grey)
BG_MAIN = "#1e293b"         # slate-800 (dark grey-blue)
BG_CARD = "#334155"         # slate-700 (card containers)
BG_CARD_HOVER = "#475569"   # slate-600
COLOR_ACCENT = "#2563eb"    # blue-600 (ASUS blue)
COLOR_TEXT_PRIMARY = "#f8fafc" # slate-50
COLOR_TEXT_MUTED = "#94a3b8"   # slate-400
COLOR_DISABLED = "#475569"

FONT_TITLE = ("Segoe UI", 16, "bold")
FONT_SUBTITLE = ("Segoe UI", 12, "bold")
FONT_LABEL = ("Segoe UI", 10, "bold")
FONT_VALUE = ("Segoe UI", 10)
FONT_MUTED = ("Segoe UI", 9)

# ==============================================================================
# CUSTOM CANVAS SLIDER
# ==============================================================================
class ModernSlider(tk.Canvas):
    def __init__(self, parent, from_=0, to=100, value=0, on_change=None, on_release=None, width=180, height=26, bg=BG_CARD, fg=COLOR_ACCENT, trough_color="#1e293b", state="normal", **kwargs):
        super().__init__(parent, width=width, height=height, bg=bg, highlightthickness=0, **kwargs)
        self.from_ = from_
        self.to = to
        self.value = value
        self.on_change = on_change
        self.on_release = on_release
        self.fg = fg
        self.trough_color = trough_color
        self.state = state
        
        self.bind("<Button-1>", self.on_click)
        self.bind("<B1-Motion>", self.on_drag)
        self.bind("<ButtonRelease-1>", self.on_mouse_release)
        self.bind("<Configure>", self.draw)
        self.width = width
        self.height = height

    def set_state(self, state):
        self.state = state
        self.draw()

    def get(self):
        return self.value

    def set(self, val):
        self.value = max(self.from_, min(self.to, val))
        self.draw()

    def draw(self, event=None):
        self.delete("all")
        w = self.winfo_width()
        h = self.winfo_height()
        if w < 10: w = self.width
        if h < 10: h = self.height
        
        y = h / 2
        r = 3  # track radius
        
        trough = self.trough_color if self.state == "normal" else "#2d3748"
        fill = self.fg if self.state == "normal" else "#4a5568"
        handle = "#ffffff" if self.state == "normal" else "#718096"
        
        # Background line
        self.create_line(12, y, w - 12, y, width=r*2, fill=trough, capstyle='round')
        
        # Fill line
        span = self.to - self.from_
        if span == 0: span = 1
        pct = (self.value - self.from_) / span
        x = 12 + pct * (w - 24)
        
        self.create_line(12, y, x, y, width=r*2, fill=fill, capstyle='round')
        
        # Knob
        hr = 6
        self.create_oval(x - hr, y - hr, x + hr, y + hr, fill=handle, outline=fill, width=2)

    def on_click(self, event):
        if self.state != "normal": return
        self.update_value(event.x)

    def on_drag(self, event):
        if self.state != "normal": return
        self.update_value(event.x)

    def on_mouse_release(self, event):
        if self.state != "normal": return
        if self.on_release:
            self.on_release(self.value)

    def update_value(self, x):
        w = self.winfo_width()
        span = self.to - self.from_
        pct = (x - 12) / (w - 24)
        pct = max(0.0, min(1.0, pct))
        self.value = int(self.from_ + pct * span)
        self.draw()
        if self.on_change:
            self.on_change(self.value)

# ==============================================================================
# CUSTOM CANVAS TOGGLE
# ==============================================================================
class ToggleSwitch(tk.Canvas):
    def __init__(self, parent, value=False, command=None, bg=BG_CARD, active_color=COLOR_ACCENT, inactive_color="#1e293b", state="normal", **kwargs):
        super().__init__(parent, width=44, height=22, bg=bg, highlightthickness=0, **kwargs)
        self.value = value
        self.command = command
        self.active_color = active_color
        self.inactive_color = inactive_color
        self.state = state
        self.bind("<Button-1>", self.on_click)
        self.draw()

    def set_state(self, state):
        self.state = state
        self.draw()

    def get(self):
        return self.value

    def set(self, val):
        self.value = bool(val)
        self.draw()

    def on_click(self, event):
        if self.state != "normal": return
        self.value = not self.value
        self.draw()
        if self.command:
            self.command(self.value)

    def draw(self):
        self.delete("all")
        w = 44
        h = 22
        
        if self.state != "normal":
            track = "#2d3748"
            knob = "#4a5568"
        else:
            track = self.active_color if self.value else self.inactive_color
            knob = "#ffffff"
            
        # Rounded track
        self.create_oval(2, 2, 20, 20, fill=track, outline="")
        self.create_oval(w - 20, 2, w - 2, 20, fill=track, outline="")
        self.create_rectangle(11, 2, w - 11, 20, fill=track, outline="")
        
        # Knob position
        kx = (w - 11) if self.value else 11
        ky = 11
        kr = 7
        self.create_oval(kx - kr, ky - kr, kx + kr, ky + kr, fill=knob, outline="")

# ==============================================================================
# CUSTOM PRESET CARD
# ==============================================================================
class PresetCard(tk.Frame):
    def __init__(self, parent, text, icon, value, command=None, bg=BG_CARD, active_bg=COLOR_ACCENT, hover_bg=BG_CARD_HOVER, **kwargs):
        super().__init__(parent, bg=bg, bd=0, padx=2, pady=4, **kwargs)
        self.text = text
        self.icon = icon
        self.value = value
        self.command = command
        self.normal_bg = bg
        self.active_bg = active_bg
        self.hover_bg = hover_bg
        self.is_active = False
        
        self.icon_label = tk.Label(self, text=icon, font=("Segoe MDL2 Assets", 15), bg=bg, fg="#ffffff", anchor="center")
        self.icon_label.pack(side="top", fill="x", pady=(4, 1))
        
        self.text_label = tk.Label(self, text=text, font=("Segoe UI", 8, "bold"), bg=bg, fg="#ffffff", anchor="center")
        self.text_label.pack(side="top", fill="x", pady=(0, 4))
        
        for w in (self, self.icon_label, self.text_label):
            w.bind("<Button-1>", self.on_click)
            w.bind("<Enter>", self.on_enter)
            w.bind("<Leave>", self.on_leave)

    def set_active(self, active):
        self.is_active = active
        color = self.active_bg if active else self.normal_bg
        self.configure(bg=color)
        self.icon_label.configure(bg=color)
        self.text_label.configure(bg=color)

    def on_click(self, event):
        if self.command:
            self.command(self.value)

    def on_enter(self, event):
        if not self.is_active:
            self.configure(bg=self.hover_bg)
            self.icon_label.configure(bg=self.hover_bg)
            self.text_label.configure(bg=self.hover_bg)

    def on_leave(self, event):
        if not self.is_active:
            self.configure(bg=self.normal_bg)
            self.icon_label.configure(bg=self.normal_bg)
            self.text_label.configure(bg=self.normal_bg)

# ==============================================================================
# MAIN APPLICATION
# ==============================================================================
class ASUSDisplayControlGUI:
    def __init__(self, root):
        self.root = root
        self.root.title("ASUS Display Control Panel")
        self.root.geometry("1100x680")
        self.root.configure(bg=BG_MAIN)
        self.root.resizable(True, True)
        self.root.minsize(1000, 640)
        
        self.dwc_path = self.find_dwc_cli()
        self.monitors = []
        self.selected_monitor_id = None
        self.current_settings = {}
        self.previous_settings = {}
        self.preset_memory = {}
        self.current_preset = None
        self.previous_preset = None
        self.is_comparing = False
        self.is_syncing = False
        
        self.build_ui()
        self.detect_monitors_thread()

    def find_dwc_cli(self):
        # 1. Search in PATH
        path = shutil.which("dwc.exe")
        if path:
            return path
        # 2. Search in workspace cli/windows/dwc/dwc.exe
        local_path = os.path.join(os.path.dirname(__file__), "cli", "windows", "dwc", "dwc.exe")
        if os.path.exists(local_path):
            return local_path
        # 3. Auto-extract packaged zip
        zip_path = os.path.join(os.path.dirname(__file__), "cli", "windows", "dwc_win.zip")
        if os.path.exists(zip_path):
            try:
                import zipfile
                dest_dir = os.path.join(os.path.dirname(__file__), "cli", "windows")
                with zipfile.ZipFile(zip_path, 'r') as zip_ref:
                    zip_ref.extractall(dest_dir)
                if os.path.exists(local_path):
                    return local_path
            except Exception:
                pass
        # 4. Search in workspace bin/dwc/dwc.exe
        local_path_bin = os.path.join(os.path.dirname(__file__), "bin", "dwc", "dwc.exe")
        if os.path.exists(local_path_bin):
            return local_path_bin
        # 5. Same folder
        same_folder = os.path.join(os.path.dirname(__file__), "dwc.exe")
        if os.path.exists(same_folder):
            return same_folder
        return None

    def run_dwc(self, args):
        if not self.dwc_path:
            raise FileNotFoundError("ASUS Display Control CLI (dwc.exe) was not found.")
        cmd = [self.dwc_path] + args
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, startupinfo=startupinfo)
        stdout, stderr = proc.communicate()
        if proc.returncode != 0:
            raise RuntimeError(stderr.strip() or stdout.strip())
        return stdout

    def build_ui(self):
        # Base Grid Layout
        self.root.grid_columnconfigure(0, weight=0) # Sidebar
        self.root.grid_columnconfigure(1, weight=1) # Main Panel
        self.root.grid_rowconfigure(0, weight=1)
        
        # ----------------------------------------------------------------------
        # SIDEBAR
        # ----------------------------------------------------------------------
        sidebar = tk.Frame(self.root, bg=BG_SIDEBAR, width=220)
        sidebar.grid(row=0, column=0, sticky="nsew")
        sidebar.grid_propagate(False)
        
        # Logo and Title
        logo_frame = tk.Frame(sidebar, bg=BG_SIDEBAR, pady=20)
        logo_frame.pack(fill="x", padx=15)
        
        logo_icon = tk.Label(logo_frame, text="🖥️", font=("Segoe UI", 24), bg=BG_SIDEBAR, fg=COLOR_ACCENT)
        logo_icon.pack(side="left")
        
        logo_text = tk.Label(logo_frame, text="ASUS\nDisplayWidget", font=("Segoe UI", 12, "bold"), bg=BG_SIDEBAR, fg="#ffffff", justify="left")
        logo_text.pack(side="left", padx=10)
        
        # Display selector dropdown
        selector_label = tk.Label(sidebar, text="SELECT MONITOR", font=FONT_MUTED, bg=BG_SIDEBAR, fg=COLOR_TEXT_MUTED)
        selector_label.pack(fill="x", padx=15, pady=(15, 2), anchor="w")
        
        self.monitor_combo = ttk.Combobox(sidebar, state="readonly", font=FONT_MUTED)
        self.monitor_combo.pack(fill="x", padx=15, pady=(0, 15))
        self.monitor_combo.bind("<<ComboboxSelected>>", self.on_monitor_selected)
        
        # Active Tab Indicator (only Splendid)
        lbl = tk.Label(sidebar, text="  Splendid", font=FONT_LABEL, bg=COLOR_ACCENT, fg="#ffffff", anchor="w", pady=6, cursor="hand2")
        lbl.pack(fill="x", padx=10, pady=5)
            
        # Status box at bottom
        self.status_lbl = tk.Label(sidebar, text="Initializing...", font=FONT_MUTED, bg=BG_SIDEBAR, fg=COLOR_TEXT_MUTED, wraplength=180, justify="left")
        self.status_lbl.pack(side="bottom", fill="x", padx=15, pady=10)
        
        # ----------------------------------------------------------------------
        # MAIN PANEL
        # ----------------------------------------------------------------------
        self.main_panel = tk.Frame(self.root, bg=BG_MAIN, padx=15, pady=15)
        self.main_panel.grid(row=0, column=1, sticky="nsew")
        
        # Header Area
        header_frame = tk.Frame(self.main_panel, bg=BG_MAIN)
        header_frame.pack(fill="x", pady=(0, 10))
        
        self.preset_title_lbl = tk.Label(header_frame, text="Splendid Presets", font=FONT_TITLE, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY)
        self.preset_title_lbl.pack(side="left")
        
        # Preset Buttons Row
        self.preset_frame = tk.Frame(self.main_panel, bg=BG_MAIN)
        self.preset_frame.pack(fill="x", pady=(0, 10))
        
        self.presets = [
            ("Standard", "\uE7F4", 4),
            ("Reading", "\uE82F", 7),
            ("Theater", "\uE8B2", 1),
            ("Scenery", "\uEB9F", 2),
            ("Game", "\uE7FC", 5),
            ("sRGB", "\uE790", 3),
            ("Darkroom", "\uEA80", 8),
            ("Night View", "\uEC46", 6)
        ]
        
        self.preset_cards = {}
        for name, icon, val in self.presets:
            card = PresetCard(self.preset_frame, name, icon, val, command=self.set_preset_thread)
            card.pack(side="left", fill="both", expand=True, padx=2)
            self.preset_cards[val] = card
            
        # Control Columns Wrapper
        cols_frame = tk.Frame(self.main_panel, bg=BG_MAIN)
        cols_frame.pack(fill="both", expand=True)
        cols_frame.grid_columnconfigure(0, weight=1)
        cols_frame.grid_columnconfigure(1, weight=1)
        
        # LEFT COLUMN - IMAGE SETTINGS
        img_frame = tk.LabelFrame(cols_frame, text=" Image Settings ", font=FONT_SUBTITLE, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY, labelanchor="n", padx=15, pady=15, bd=1, relief="solid")
        img_frame.grid(row=0, column=0, sticky="nsew", padx=(0, 10))
        img_frame.grid_columnconfigure(1, weight=1)
        
        # Brightness
        tk.Label(img_frame, text="Brightness", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=0, column=0, sticky="w", pady=8)
        self.val_brightness_lbl = tk.Label(img_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_brightness_lbl.grid(row=0, column=2, sticky="e")
        self.slider_brightness = ModernSlider(img_frame, from_=0, to=100, on_change=lambda v: self.val_brightness_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("Brightness", v))
        self.slider_brightness.grid(row=0, column=1, sticky="ew", padx=10)
        
        # Contrast
        tk.Label(img_frame, text="Contrast", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=1, column=0, sticky="w", pady=8)
        self.val_contrast_lbl = tk.Label(img_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_contrast_lbl.grid(row=1, column=2, sticky="e")
        self.slider_contrast = ModernSlider(img_frame, from_=0, to=100, on_change=lambda v: self.val_contrast_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("Contrast", v))
        self.slider_contrast.grid(row=1, column=1, sticky="ew", padx=10)
        
        # Trace Free (Overdrive)
        tk.Label(img_frame, text="Trace Free", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=2, column=0, sticky="w", pady=8)
        self.val_overdrive_lbl = tk.Label(img_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_overdrive_lbl.grid(row=2, column=2, sticky="e")
        self.slider_overdrive = ModernSlider(img_frame, from_=0, to=100, on_change=lambda v: self.val_overdrive_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("Overdrive", v))
        self.slider_overdrive.grid(row=2, column=1, sticky="ew", padx=10)
        
        # Shadow Boost dropdown
        tk.Label(img_frame, text="Shadow Boost", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=3, column=0, sticky="w", pady=10)
        self.combo_shadowboost = ttk.Combobox(img_frame, state="readonly", values=["OFF", "Level 1", "Level 2", "Level 3"], font=FONT_MUTED)
        self.combo_shadowboost.grid(row=3, column=1, columnspan=2, sticky="ew", padx=10)
        self.combo_shadowboost.bind("<<ComboboxSelected>>", lambda e: self.set_vcp_value_thread("ShadowBoost", ["OFF", "Level 1", "Level 2", "Level 3"].index(self.combo_shadowboost.get())))
        
        # ASCR Switch
        tk.Label(img_frame, text="ASCR", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=4, column=0, sticky="w", pady=10)
        self.switch_ascr = ToggleSwitch(img_frame, command=lambda v: self.set_vcp_value_thread("ASCR", 1 if v else 0))
        self.switch_ascr.grid(row=4, column=1, sticky="w", padx=10)
        
        # RIGHT COLUMN - COLOR SETTINGS
        col_frame = tk.LabelFrame(cols_frame, text=" Color Settings ", font=FONT_SUBTITLE, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY, labelanchor="n", padx=15, pady=15, bd=1, relief="solid")
        col_frame.grid(row=0, column=1, sticky="nsew", padx=(10, 0))
        col_frame.grid_columnconfigure(1, weight=1)
        
        # Saturation
        tk.Label(col_frame, text="Saturation", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=0, column=0, sticky="w", pady=8)
        self.val_saturation_lbl = tk.Label(col_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_saturation_lbl.grid(row=0, column=2, sticky="e")
        self.slider_saturation = ModernSlider(col_frame, from_=0, to=100, on_change=lambda v: self.val_saturation_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("Saturation", v))
        self.slider_saturation.grid(row=0, column=1, sticky="ew", padx=10)
        
        # Hue
        tk.Label(col_frame, text="Hue", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=1, column=0, sticky="w", pady=8)
        self.val_hue_lbl = tk.Label(col_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_hue_lbl.grid(row=1, column=2, sticky="e")
        self.slider_hue = ModernSlider(col_frame, from_=0, to=100, on_change=lambda v: self.val_hue_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("Hue", v))
        self.slider_hue.grid(row=1, column=1, sticky="ew", padx=10)
        
        # Color Temp dropdown
        tk.Label(col_frame, text="Color Temp.", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=2, column=0, sticky="w", pady=10)
        self.temp_values = ["4000K", "5000K", "6500K (Warm)", "7500K", "8200K", "9300K (Cool)", "10000K", "User"]
        self.temp_codes = [3, 4, 5, 6, 7, 8, 9, 11]
        self.combo_temp = ttk.Combobox(col_frame, state="readonly", values=self.temp_values, font=FONT_MUTED)
        self.combo_temp.grid(row=2, column=1, columnspan=2, sticky="ew", padx=10)
        self.combo_temp.bind("<<ComboboxSelected>>", self.on_temp_selected)
        
        # R, G, B Gain Sliders
        tk.Label(col_frame, text="Red Gain", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=3, column=0, sticky="w", pady=6)
        self.val_r_lbl = tk.Label(col_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_r_lbl.grid(row=3, column=2, sticky="e")
        self.slider_r = ModernSlider(col_frame, from_=0, to=100, on_change=lambda v: self.val_r_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("RedGain", v))
        self.slider_r.grid(row=3, column=1, sticky="ew", padx=10)
        
        tk.Label(col_frame, text="Green Gain", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=4, column=0, sticky="w", pady=6)
        self.val_g_lbl = tk.Label(col_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_g_lbl.grid(row=4, column=2, sticky="e")
        self.slider_g = ModernSlider(col_frame, from_=0, to=100, on_change=lambda v: self.val_g_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("GreenGain", v))
        self.slider_g.grid(row=4, column=1, sticky="ew", padx=10)
        
        tk.Label(col_frame, text="Blue Gain", font=FONT_LABEL, bg=BG_MAIN, fg=COLOR_TEXT_PRIMARY).grid(row=5, column=0, sticky="w", pady=6)
        self.val_b_lbl = tk.Label(col_frame, text="--", font=FONT_VALUE, bg=BG_MAIN, fg=COLOR_TEXT_MUTED)
        self.val_b_lbl.grid(row=5, column=2, sticky="e")
        self.slider_b = ModernSlider(col_frame, from_=0, to=100, on_change=lambda v: self.val_b_lbl.configure(text=str(v)), on_release=lambda v: self.set_vcp_value_thread("BlueGain", v))
        self.slider_b.grid(row=5, column=1, sticky="ew", padx=10)
        
        # BOTTOM ACTION ROW
        actions_frame = tk.Frame(self.main_panel, bg=BG_MAIN)
        actions_frame.pack(fill="x", pady=(15, 0))
        
        # Left Actions
        self.btn_reset = tk.Button(actions_frame, text="Reset Mode", font=FONT_LABEL, bg=BG_CARD, fg="#ffffff", activebackground=BG_CARD_HOVER, activeforeground="#ffffff", bd=0, padx=15, pady=6, cursor="hand2", command=self.reset_preset_thread)
        self.btn_reset.pack(side="left", padx=(0, 10))
        
        self.btn_compare = tk.Button(actions_frame, text="Compare Settings", font=FONT_LABEL, bg=BG_CARD, fg="#ffffff", activebackground=COLOR_ACCENT, activeforeground="#ffffff", bd=0, padx=15, pady=6, cursor="hand2")
        self.btn_compare.pack(side="left")
        self.btn_compare.bind("<Button-1>", self.start_compare)
        self.btn_compare.bind("<ButtonRelease-1>", self.stop_compare)
        
        # Right Actions
        self.btn_export = tk.Button(actions_frame, text="Export Profile", font=FONT_LABEL, bg=COLOR_ACCENT, fg="#ffffff", activebackground="#1d4ed8", activeforeground="#ffffff", bd=0, padx=15, pady=6, cursor="hand2", command=self.export_profile)
        self.btn_export.pack(side="right", padx=(10, 0))
        
        self.btn_import = tk.Button(actions_frame, text="Import Profile", font=FONT_LABEL, bg=BG_CARD, fg="#ffffff", activebackground=BG_CARD_HOVER, activeforeground="#ffffff", bd=0, padx=15, pady=6, cursor="hand2", command=self.import_profile)
        self.btn_import.pack(side="right")

    # ==============================================================================
    # CONTROLLER METHODS (THREADED OPERATIONS)
    # ==============================================================================
    def set_status(self, msg):
        self.root.after(0, lambda: self.status_lbl.configure(text=msg))
        
    def detect_monitors_thread(self):
        self.set_status("Searching for ASUS monitors...")
        threading.Thread(target=self.detect_monitors, daemon=True).start()
        
    def detect_monitors(self):
        try:
            out = self.run_dwc(["list"])
            lines = [l.strip() for l in out.split("\n") if l.strip()]
            monitors = []
            
            # Parse list output
            for line in lines:
                if line.startswith("ID") or line.startswith("--") or line.startswith("Detected"):
                    continue
                parts = [p for p in line.split(" ") if p]
                if len(parts) >= 2:
                    m_id = parts[0]
                    model = parts[1]
                    monitors.append({"id": m_id, "model": model})
                    
            self.monitors = monitors
            
            if not monitors:
                self.root.after(0, lambda: self.set_status("No monitors found. Check connection."))
                return
            
            combo_vals = [f"{m['id']} - {m['model']}" for m in monitors]
            self.root.after(0, lambda: self.populate_monitors(combo_vals))
            
        except Exception as e:
            self.root.after(0, lambda: self.set_status(f"Error: {str(e)}"))

    def populate_monitors(self, values):
        self.monitor_combo.configure(values=values)
        self.monitor_combo.current(0)
        self.on_monitor_selected()

    def on_monitor_selected(self, event=None):
        selected = self.monitor_combo.get()
        if not selected: return
        self.selected_monitor_id = selected.split(" - ")[0]
        self.query_settings_thread()

    def query_settings_thread(self):
        if self.is_syncing: return
        self.is_syncing = True
        self.set_status("Syncing with monitor settings...")
        threading.Thread(target=self.query_settings, daemon=True).start()

    def query_settings(self):
        m_id = self.selected_monitor_id
        if not hasattr(self, "supported_properties"):
            self.supported_properties = {}
            
        all_props = ["Splendid", "Brightness", "Contrast", "Overdrive", "ShadowBoost", "ASCR", "Saturation", "Hue", "ColorTemp", "RedGain", "GreenGain", "BlueGain"]
        settings = {}
        
        # If this is the first time syncing this monitor, run queries in parallel
        if m_id not in self.supported_properties:
            results = {}
            threads = []
            def query_one(prop):
                try:
                    out = self.run_dwc(["get", prop, "--id", m_id])
                    val = int(out.strip())
                    results[prop] = val
                except Exception:
                    results[prop] = None
                    
            for prop in all_props:
                t = threading.Thread(target=query_one, args=(prop,))
                t.start()
                threads.append(t)
            for t in threads:
                t.join()
                
            supported = []
            for prop in all_props:
                val = results.get(prop)
                settings[prop] = val
                if val is not None:
                    supported.append(prop)
            self.supported_properties[m_id] = supported
        else:
            # Query only supported properties sequentially
            query_props = self.supported_properties[m_id]
            for prop in query_props:
                # Optimize: Skip RGB gains if ColorTemp is not set to User (11)
                if prop in ["RedGain", "GreenGain", "BlueGain"]:
                    color_temp = settings.get("ColorTemp") or self.current_settings.get("ColorTemp")
                    if color_temp != 11:
                        settings[prop] = None
                        continue
                try:
                    out = self.run_dwc(["get", prop, "--id", m_id])
                    val = int(out.strip())
                    settings[prop] = val
                except Exception:
                    settings[prop] = None
                    
            # Populate un-queried properties with None
            for prop in all_props:
                if prop not in settings:
                    settings[prop] = None
                    
        preset_changed = False
        old_preset = self.current_settings.get("Splendid")
        new_preset = settings.get("Splendid")
        if old_preset is not None and new_preset is not None and old_preset != new_preset:
            preset_changed = True
            
        # Update preset history trackers
        if new_preset is not None:
            if self.current_preset is None:
                self.current_preset = new_preset
            elif self.current_preset != new_preset:
                self.previous_preset = self.current_preset
                self.current_preset = new_preset
            
        self.current_settings = settings.copy()
        if not self.previous_settings or preset_changed:
            self.previous_settings = settings.copy()
            
        # Initialize memory with baseline values if not already present
        if new_preset is not None:
            if new_preset not in self.preset_memory:
                self.preset_memory[new_preset] = {}
                for prop, val in settings.items():
                    if val is not None and prop != "Splendid":
                        self.preset_memory[new_preset][prop] = val
            
        self.root.after(0, self.update_ui_state)

    def update_ui_state(self):
        self.is_syncing = False
        self.set_status("Settings synchronized.")
        
        # 1. Update Preset Cards
        active_preset = self.current_settings.get("Splendid")
        for val, card in self.preset_cards.items():
            card.set_active(val == active_preset)
            
        # 2. Update Image Settings
        self.update_slider(self.slider_brightness, self.val_brightness_lbl, "Brightness")
        self.update_slider(self.slider_contrast, self.val_contrast_lbl, "Contrast")
        self.update_slider(self.slider_overdrive, self.val_overdrive_lbl, "Overdrive")
        
        # Shadow Boost dropdown
        sb = self.current_settings.get("ShadowBoost")
        if sb is not None and 0 <= sb < 4:
            self.combo_shadowboost.configure(state="readonly")
            self.combo_shadowboost.set(["OFF", "Level 1", "Level 2", "Level 3"][sb])
        else:
            self.combo_shadowboost.set("Unsupported")
            self.combo_shadowboost.configure(state="disabled")
            
        # ASCR Switch
        ascr = self.current_settings.get("ASCR")
        if ascr is not None:
            self.switch_ascr.set_state("normal")
            self.switch_ascr.set(ascr == 1)
        else:
            self.switch_ascr.set(False)
            self.switch_ascr.set_state("disabled")
            
        # 3. Update Color Settings
        self.update_slider(self.slider_saturation, self.val_saturation_lbl, "Saturation")
        self.update_slider(self.slider_hue, self.val_hue_lbl, "Hue")
        
        # Color Temp dropdown
        ct = self.current_settings.get("ColorTemp")
        if ct is not None and ct in self.temp_codes:
            self.combo_temp.configure(state="readonly")
            self.combo_temp.set(self.temp_values[self.temp_codes.index(ct)])
        else:
            self.combo_temp.set("Unsupported")
            self.combo_temp.configure(state="disabled")
            
        # R, G, B gains
        self.update_slider(self.slider_r, self.val_r_lbl, "RedGain")
        self.update_slider(self.slider_g, self.val_g_lbl, "GreenGain")
        self.update_slider(self.slider_b, self.val_b_lbl, "BlueGain")

    def update_slider(self, slider, label, prop_name):
        val = self.current_settings.get(prop_name)
        if val is not None:
            slider.set_state("normal")
            slider.set(val)
            label.configure(text=str(val), fg=COLOR_TEXT_PRIMARY)
        else:
            slider.set(0)
            slider.set_state("disabled")
            label.configure(text="--", fg=COLOR_DISABLED)

    # ==============================================================================
    # SET OPERATIONS (THREADED)
    # ==============================================================================
    def set_preset_thread(self, val):
        if not self.selected_monitor_id:
            self.set_status("Error: No monitor selected.")
            return
        if self.is_syncing or self.is_comparing: return
        
        # Update preset history on manual selection
        if self.current_preset is not None and self.current_preset != val:
            self.previous_preset = self.current_preset
            self.current_preset = val
            
        self.set_status(f"Changing Splendid mode to {val}...")
        self.is_syncing = True
        
        # Deactivate all cards temporarily
        for card in self.preset_cards.values():
            card.set_active(False)
        self.preset_cards[val].set_active(True)
        
        threading.Thread(target=self.set_preset, args=(val,), daemon=True).start()

    def set_preset(self, val):
        try:
            self.run_dwc(["set", "Splendid", str(val), "--id", self.selected_monitor_id])
            # Sleep slightly to allow monitor to transition before reading/writing settings
            time.sleep(0.2)
            
            # Apply saved memory settings in dependency order: ColorTemp first, RGB Gains last.
            if val in self.preset_memory:
                sorted_props = []
                # 1. ColorTemp
                if "ColorTemp" in self.preset_memory[val]:
                    sorted_props.append(("ColorTemp", self.preset_memory[val]["ColorTemp"]))
                # 2. Other non-gain properties
                for prop, saved_val in self.preset_memory[val].items():
                    if prop not in ["ColorTemp", "RedGain", "GreenGain", "BlueGain"]:
                        sorted_props.append((prop, saved_val))
                # 3. RGB Gains
                for prop in ["RedGain", "GreenGain", "BlueGain"]:
                    if prop in self.preset_memory[val]:
                        sorted_props.append((prop, self.preset_memory[val][prop]))
                        
                for prop, saved_val in sorted_props:
                    if saved_val is not None:
                        try:
                            self.run_dwc(["set", prop, str(saved_val), "--id", self.selected_monitor_id])
                            time.sleep(0.05)
                        except Exception:
                            pass
            
            self.query_settings()
        except Exception as e:
            self.root.after(0, lambda: self.set_status(f"Error: {str(e)}"))
            self.is_syncing = False

    def set_vcp_value_thread(self, prop_name, val):
        if not self.selected_monitor_id:
            self.set_status("Error: No monitor selected.")
            return
        if self.is_syncing or self.is_comparing: return
        self.previous_settings = self.current_settings.copy()
        self.current_settings[prop_name] = val
        
        # Save to preset memory
        current_preset = self.current_settings.get("Splendid")
        if current_preset is not None:
            if current_preset not in self.preset_memory:
                self.preset_memory[current_preset] = {}
            self.preset_memory[current_preset][prop_name] = val
            
        self.set_status(f"Updating {prop_name} to {val}...")
        threading.Thread(target=self.set_vcp_value, args=(prop_name, val), daemon=True).start()

    def set_vcp_value(self, prop_name, val):
        try:
            self.run_dwc(["set", prop_name, str(val), "--id", self.selected_monitor_id])
            self.root.after(0, lambda: self.set_status(f"{prop_name} updated successfully."))
        except Exception as e:
            self.root.after(0, lambda: self.set_status(f"Error updating {prop_name}: {str(e)}"))

    def on_temp_selected(self, event=None):
        temp_str = self.combo_temp.get()
        if temp_str not in self.temp_values: return
        val = self.temp_codes[self.temp_values.index(temp_str)]
        self.set_vcp_value_thread("ColorTemp", val)

    # ==============================================================================
    # EXTRA ACTION HANDLERS
    # ==============================================================================
    def reset_preset_thread(self):
        if not self.selected_monitor_id:
            self.set_status("Error: No monitor selected.")
            return
        if self.is_syncing or self.is_comparing: return
        if not messagebox.askyesno("Reset Mode", "Are you sure you want to reset the current display settings to factory default?"):
            return
        self.is_syncing = True
        self.set_status("Resetting monitor settings...")
        threading.Thread(target=self.reset_preset, daemon=True).start()

    def reset_preset(self):
        try:
            current_preset = self.current_settings.get("Splendid")
            if current_preset is not None and current_preset in self.preset_memory:
                del self.preset_memory[current_preset]
                
            self.run_dwc(["reset-all", "--id", self.selected_monitor_id])
            time.sleep(2.0) # Wait for display reset cycle
            self.query_settings()
        except Exception as e:
            self.root.after(0, lambda: self.set_status(f"Error: {str(e)}"))
            self.is_syncing = False

    def start_compare(self, event=None):
        if not self.selected_monitor_id: return
        if self.is_syncing: return
        if not self.previous_preset: return
        
        self.is_comparing = True
        self.set_status("Comparing: Showing previous preset...")
        self.btn_compare.configure(bg=COLOR_ACCENT, text="Comparing...")
        
        # UI cards highlight the previous preset
        for card in self.preset_cards.values():
            card.set_active(False)
        self.preset_cards[self.previous_preset].set_active(True)
        
        threading.Thread(target=self.set_preset_compare, args=(self.previous_preset,), daemon=True).start()

    def stop_compare(self, event=None):
        if not self.selected_monitor_id: return
        if not self.is_comparing: return
        
        self.is_comparing = False
        self.set_status("Restoring current preset...")
        self.btn_compare.configure(bg=BG_CARD, text="Compare Settings")
        
        # UI cards highlight the current preset
        for card in self.preset_cards.values():
            card.set_active(False)
        if self.current_preset in self.preset_cards:
            self.preset_cards[self.current_preset].set_active(True)
            
        self.is_syncing = True
        threading.Thread(target=self.set_preset, args=(self.current_preset,), daemon=True).start()

    def set_preset_compare(self, val):
        try:
            self.run_dwc(["set", "Splendid", str(val), "--id", self.selected_monitor_id])
            time.sleep(0.2)
            
            # Apply saved memory settings in dependency order: ColorTemp first, RGB Gains last.
            if val in self.preset_memory:
                sorted_props = []
                # 1. ColorTemp
                if "ColorTemp" in self.preset_memory[val]:
                    sorted_props.append(("ColorTemp", self.preset_memory[val]["ColorTemp"]))
                # 2. Other non-gain properties
                for prop, saved_val in self.preset_memory[val].items():
                    if prop not in ["ColorTemp", "RedGain", "GreenGain", "BlueGain"]:
                        sorted_props.append((prop, saved_val))
                # 3. RGB Gains
                for prop in ["RedGain", "GreenGain", "BlueGain"]:
                    if prop in self.preset_memory[val]:
                        sorted_props.append((prop, self.preset_memory[val][prop]))
                        
                for prop, saved_val in sorted_props:
                    if saved_val is not None:
                        try:
                            self.run_dwc(["set", prop, str(saved_val), "--id", self.selected_monitor_id])
                            time.sleep(0.05)
                        except Exception:
                            pass
            
            # Read and temporarily update UI state
            preset_vals = self.current_settings.copy()
            preset_vals["Splendid"] = val
            if val in self.preset_memory:
                for k, v in self.preset_memory[val].items():
                    preset_vals[k] = v
            else:
                for k in preset_vals.keys():
                    if k != "Splendid":
                        preset_vals[k] = None
                        
            actual_settings = self.current_settings.copy()
            
            def run_ui_update():
                self.current_settings = preset_vals
                self.update_ui_state()
                self.current_settings = actual_settings
                self.set_status("Comparing: Showing previous preset (Release to restore)...")
                
            self.root.after(0, run_ui_update)
        except Exception as e:
            self.root.after(0, lambda: self.set_status(f"Error comparing: {str(e)}"))

    def export_profile(self):
        if not self.selected_monitor_id:
            self.set_status("Error: No monitor selected.")
            return
        if not self.current_settings: return
        file_path = filedialog.asksaveasfilename(defaultextension=".json", filetypes=[("JSON Files", "*.json")], title="Export Settings Profile")
        if not file_path: return
        
        try:
            with open(file_path, "w") as f:
                json.dump(self.current_settings, f, indent=4)
            self.set_status("Profile exported successfully.")
        except Exception as e:
            messagebox.showerror("Export Failed", f"Could not export profile: {str(e)}")
  
    def import_profile(self):
        if not self.selected_monitor_id:
            self.set_status("Error: No monitor selected.")
            return
        if self.is_syncing: return
        file_path = filedialog.askopenfilename(filetypes=[("JSON Files", "*.json")], title="Import Settings Profile")
        if not file_path: return
        
        try:
            with open(file_path, "r") as f:
                imported = json.load(f)
                
            self.is_syncing = True
            self.set_status("Applying imported profile...")
            
            threading.Thread(target=self.apply_imported_profile, args=(imported,), daemon=True).start()
            
        except Exception as e:
            messagebox.showerror("Import Failed", f"Could not import profile: {str(e)}")
            self.is_syncing = False

    def apply_imported_profile(self, settings_dict):
        try:
            m_id = self.selected_monitor_id
            
            # If Splendid preset is specified, switch to it first
            imported_preset = settings_dict.get("Splendid")
            if imported_preset is not None:
                try:
                    self.run_dwc(["set", "Splendid", str(imported_preset), "--id", m_id])
                    time.sleep(0.2)
                except Exception:
                    pass
            
            # Save all settings to preset memory for this preset
            current_preset = imported_preset or self.current_settings.get("Splendid")
            if current_preset is not None:
                if current_preset not in self.preset_memory:
                    self.preset_memory[current_preset] = {}
                for prop, val in settings_dict.items():
                    if val is not None and prop != "Splendid":
                        self.preset_memory[current_preset][prop] = val
                        
            # Apply each individual setting in dependency order: ColorTemp first, RGB Gains last.
            sorted_props = []
            if "ColorTemp" in settings_dict:
                sorted_props.append(("ColorTemp", settings_dict["ColorTemp"]))
            for prop, val in settings_dict.items():
                if prop not in ["ColorTemp", "RedGain", "GreenGain", "BlueGain", "Splendid"]:
                    sorted_props.append((prop, val))
            for prop in ["RedGain", "GreenGain", "BlueGain"]:
                if prop in settings_dict:
                    sorted_props.append((prop, settings_dict[prop]))
                    
            for prop, val in sorted_props:
                if val is not None:
                    try:
                        self.run_dwc(["set", prop, str(val), "--id", m_id])
                        time.sleep(0.05)
                    except Exception:
                        pass
                        
            self.query_settings()
        except Exception as e:
            self.root.after(0, lambda: self.set_status(f"Error applying profile: {str(e)}"))
            self.is_syncing = False

# ==============================================================================
# ENTRY POINT
# ==============================================================================
if __name__ == "__main__":
    # Apply high DPI scaling for Windows
    if os.name == 'nt':
        try:
            import ctypes
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            pass
            
    root = tk.Tk()
    
    # Simple custom styling for Combobox dropdowns to look dark themed
    style = ttk.Style()
    style.theme_use('clam')
    style.configure("TCombobox", fieldbackground="#1e293b", background="#334155", foreground="#ffffff", arrowcolor="#ffffff")
    style.map("TCombobox", fieldbackground=[('readonly', '#1e293b')], foreground=[('readonly', '#ffffff')])
    
    app = ASUSDisplayControlGUI(root)
    root.mainloop()
