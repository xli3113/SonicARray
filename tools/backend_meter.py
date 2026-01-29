#!/usr/bin/env python3
"""
Backend 可视化 Meter
- 发送声源位置 OSC 到后端 (127.0.0.1:7000)
- 显示 28 路扬声器增益条（模拟值，基于距离）
- 可选：若后端开启增益 OSC 输出，可显示真实增益（见文档）

用法:
    python tools/backend_meter.py [speakers.yaml]
    
依赖: 仅标准库 + tkinter（Python 自带）
"""

import socket
import struct
import math
import re
import sys
import os

# 尝试用 tkinter（Python 自带）
try:
    import tkinter as tk
    from tkinter import ttk, messagebox
except ImportError:
    print("需要 tkinter。Windows/macOS 自带；Linux: sudo apt install python3-tk")
    sys.exit(1)


def send_osc(ip, port, address, *args):
    msg = address.encode("ascii")
    msg += b"\x00" * (4 - (len(msg) % 4))
    type_tag = "," + "".join("f" if isinstance(a, float) else "i" for a in args)
    msg += type_tag.encode("ascii")
    msg += b"\x00" * (4 - (len(type_tag) % 4))
    for arg in args:
        if isinstance(arg, float):
            msg += struct.pack(">f", arg)
        elif isinstance(arg, int):
            msg += struct.pack(">i", arg)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.sendto(msg, (ip, port))
    sock.close()


def load_speakers_yaml(path):
    """简单解析 speakers.yaml，返回 [(id, x, y, z), ...]"""
    speakers = []
    if not os.path.isfile(path):
        return speakers
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    # 匹配 - id: N 和 x/y/z
    block = re.findall(
        r"-\s*id:\s*(\d+)\s*\n\s*x:\s*([-\d.]+)\s*\n\s*y:\s*([-\d.]+)\s*\n\s*z:\s*([-\d.]+)",
        content,
    )
    for mid, mx, my, mz in block:
        speakers.append((int(mid), float(mx), float(my), float(mz)))
    return speakers


def simulated_gains(speakers, sx, sy, sz):
    """基于距离的模拟增益（与 Unity 端近似）：1/(1+d^2)，再取前几路归一化"""
    gains = []
    for _id, x, y, z in speakers:
        d = math.sqrt((sx - x) ** 2 + (sy - y) ** 2 + (sz - z) ** 2)
        g = 1.0 / (1.0 + d * d)
        gains.append((_id, g))
    # 按增益排序，取前 3 做 VBAP 风格归一化（可选）
    gains.sort(key=lambda t: -t[1])
    total = sum(g for _, g in gains[:3]) or 1.0
    out = [0.0] * len(speakers)
    for i, (idx, g) in enumerate(gains):
        if i < 3 and total > 0:
            out[idx] = g / total
        else:
            out[idx] = 0.0
    return out


class MeterApp:
    def __init__(self, yaml_path="speakers.yaml"):
        self.osc_ip = "127.0.0.1"
        self.osc_port = 7000
        self.speakers = load_speakers_yaml(yaml_path)
        if not self.speakers:
            # 尝试项目根目录
            base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            self.speakers = load_speakers_yaml(os.path.join(base, "speakers.yaml"))
        if not self.speakers:
            self.speakers = [(i, -2.0 + i * 0.15, 1.5, 0.0) for i in range(28)]
        self.source = [0.0, 1.5, 2.0]
        self.gains = [0.0] * len(self.speakers)
        self.send_interval_ms = 80  # ~12 Hz
        self.root = tk.Tk()
        self.root.title("SoundARray Backend Meter")
        self.root.geometry("720x420")
        self._build_ui()
        self._schedule_send()
        self._schedule_meter()

    def _build_ui(self):
        f = ttk.Frame(self.root, padding=8)
        f.pack(fill=tk.BOTH, expand=True)
        # 声源位置
        pos_f = ttk.LabelFrame(f, text="声源位置 (发送到 Backend OSC 7000)")
        pos_f.pack(fill=tk.X, pady=(0, 6))
        self._vars = []
        for i, (name, default) in enumerate([("X", 0.0), ("Y", 1.5), ("Z", 2.0)]):
            row = ttk.Frame(pos_f)
            row.pack(fill=tk.X)
            ttk.Label(row, text=name, width=2).pack(side=tk.LEFT, padx=2)
            var = tk.DoubleVar(value=default)
            self._vars.append(var)
            s = ttk.Scale(row, from_=-3, to=3, orient=tk.HORIZONTAL, length=200, variable=var, command=lambda _: self._sync_source())
            s.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=4)
            self.source[i] = default
            l = ttk.Label(row, text=f"{default:.2f}", width=6)
            l.pack(side=tk.LEFT)
            setattr(self, f"_pos_label_{i}", l)
        # 28 路 Meter
        meter_f = ttk.LabelFrame(f, text="扬声器增益 (模拟，基于距离)")
        meter_f.pack(fill=tk.BOTH, expand=True, pady=6)
        canvas_f = ttk.Frame(meter_f)
        canvas_f.pack(fill=tk.BOTH, expand=True)
        self.canvas = tk.Canvas(canvas_f, height=180, bg="#1e1e1e", highlightthickness=0)
        self.canvas.pack(fill=tk.BOTH, expand=True)
        self.bar_rects = []
        self.bar_labels = []
        n = len(self.speakers)
        w = 720 - 40
        bar_w = max(4, (w // n) - 2)
        for i in range(n):
            x0 = 20 + i * (bar_w + 2)
            r = self.canvas.create_rectangle(x0, 160, x0 + bar_w, 160, fill="#0a7", outline="#055")
            self.bar_rects.append(r)
            t = self.canvas.create_text(x0 + bar_w // 2, 172, text=str(i), fill="#aaa", font=("", 8))
            self.bar_labels.append(t)
        ttk.Label(f, text="提示: 先启动 Backend，再移动滑块。Backend 控制台会打印当前激活扬声器。", foreground="gray").pack(anchor=tk.W)

    def _sync_source(self):
        for i, v in enumerate(self._vars):
            self.source[i] = v.get()
            getattr(self, f"_pos_label_{i}").config(text=f"{self.source[i]:.2f}")

    def _schedule_send(self):
        self._sync_source()
        send_osc(self.osc_ip, self.osc_port, "/spatial/source_pos", self.source[0], self.source[1], self.source[2])
        self.root.after(self.send_interval_ms, self._schedule_send)

    def _schedule_meter(self):
        self.gains = simulated_gains(self.speakers, self.source[0], self.source[1], self.source[2])
        n = len(self.speakers)
        w = 720 - 40
        bar_w = max(4, (w // n) - 2)
        for i in range(n):
            r = self.bar_rects[i]
            x0 = 20 + i * (bar_w + 2)
            h = max(1, int(self.gains[i] * 150))
            self.canvas.coords(r, x0, 160, x0 + bar_w, 160 - h)
            self.canvas.itemconfig(r, fill="#0a7" if self.gains[i] > 0.01 else "#333")
        self.root.after(100, self._schedule_meter)

    def run(self):
        self.root.mainloop()


def main():
    yaml_path = sys.argv[1] if len(sys.argv) > 1 else "speakers.yaml"
    if not os.path.isfile(yaml_path):
        base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        yaml_path = os.path.join(base, "speakers.yaml")
    app = MeterApp(yaml_path)
    app.run()


if __name__ == "__main__":
    main()
