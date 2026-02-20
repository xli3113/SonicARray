#!/usr/bin/env python3
"""
OSC 测试发送工具
用于在没有 Unity/Quest 的情况下测试 C++ 后端

使用方法:
    python3 test_osc_sender.py [模式]

也可用 Pure Data: 打开 tools/test_osc_soundarray.pd

模式:
    manual    - 手动输入位置（默认）
    circle    - 圆形运动
    line      - 直线运动
    random    - 随机位置
    multi     - 多声源模式（同时发送 source 0,1,2 位置）
"""

import socket
import struct
import time
import sys
import math
import random

def send_osc(ip, port, address, *args):
    """发送 OSC 消息"""
    # 构建 OSC 消息
    msg = address.encode('ascii')
    msg += b'\x00' * (4 - (len(msg) % 4))  # 对齐到 4 字节
    
    # 类型标签
    type_tag = ',' + ''.join(['f' if isinstance(a, float) else 'i' for a in args])
    msg += type_tag.encode('ascii')
    msg += b'\x00' * (4 - (len(type_tag) % 4))
    
    # 参数
    for arg in args:
        if isinstance(arg, float):
            msg += struct.pack('>f', arg)
        elif isinstance(arg, int):
            msg += struct.pack('>i', arg)
    
    # 发送
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.sendto(msg, (ip, port))
    sock.close()

def manual_mode(ip, port):
    """手动输入模式"""
    print("手动模式 - 输入位置 (x y z)，或 'q' 退出")
    while True:
        try:
            line = input("位置 (x y z): ").strip()
            if line.lower() == 'q':
                break
            parts = line.split()
            if len(parts) == 3:
                x, y, z = float(parts[0]), float(parts[1]), float(parts[2])
                send_osc(ip, port, '/spatial/source_pos', x, y, z)
                print(f"✓ 发送: ({x}, {y}, {z})")
            else:
                print("错误: 需要 3 个数字 (x y z)")
        except ValueError:
            print("错误: 无效的数字")
        except KeyboardInterrupt:
            break

def circle_mode(ip, port, radius=2.0, height=1.5, duration=10.0):
    """圆形运动模式"""
    print(f"圆形运动模式 - 半径={radius}, 高度={height}, 持续时间={duration}秒")
    steps = int(duration * 10)  # 10 Hz
    for i in range(steps):
        angle = 2 * math.pi * i / steps
        x = radius * math.cos(angle)
        z = radius * math.sin(angle)
        y = height
        send_osc(ip, port, '/spatial/source_pos', x, y, z)
        print(f"发送: ({x:.2f}, {y:.2f}, {z:.2f}) - 角度: {math.degrees(angle):.1f}°")
        time.sleep(0.1)

def line_mode(ip, port, start=(-2, 1.5, 2), end=(2, 1.5, 2), duration=5.0):
    """直线运动模式"""
    print(f"直线运动模式 - 从 {start} 到 {end}, 持续时间={duration}秒")
    steps = int(duration * 10)  # 10 Hz
    for i in range(steps):
        t = i / steps
        x = start[0] + (end[0] - start[0]) * t
        y = start[1] + (end[1] - start[1]) * t
        z = start[2] + (end[2] - start[2]) * t
        send_osc(ip, port, '/spatial/source_pos', x, y, z)
        print(f"发送: ({x:.2f}, {y:.2f}, {z:.2f}) - 进度: {t*100:.1f}%")
        time.sleep(0.1)

def multi_mode(ip, port, duration=10.0):
    """多声源模式：同时发送 3 个声源的位置 (sourceId, x, y, z)"""
    print(f"多声源模式 - 发送 source 0, 1, 2，持续 {duration} 秒")
    steps = int(duration * 10)
    for i in range(steps):
        t = i / steps
        send_osc(ip, port, '/spatial/source_pos', 0, 2*math.cos(2*math.pi*t), 1.5, 2*math.sin(2*math.pi*t))
        send_osc(ip, port, '/spatial/source_pos', 1, -2*math.cos(2*math.pi*t), 1.5, -2*math.sin(2*math.pi*t))
        send_osc(ip, port, '/spatial/source_pos', 2, 0, 2.0, 1.5 + math.sin(4*math.pi*t))
        print(f"[{i+1}/{steps}] 发送 3 个声源")
        time.sleep(0.1)

def random_mode(ip, port, count=20, bounds=((-2, 2), (0.5, 2.5), (0, 3))):
    """随机位置模式"""
    print(f"随机位置模式 - {count} 个随机位置")
    for i in range(count):
        x = random.uniform(bounds[0][0], bounds[0][1])
        y = random.uniform(bounds[1][0], bounds[1][1])
        z = random.uniform(bounds[2][0], bounds[2][1])
        send_osc(ip, port, '/spatial/source_pos', x, y, z)
        print(f"[{i+1}/{count}] 发送: ({x:.2f}, {y:.2f}, {z:.2f})")
        time.sleep(0.5)

def main():
    ip = '127.0.0.1'
    port = 7000
    
    if len(sys.argv) > 1:
        mode = sys.argv[1].lower()
    else:
        mode = 'manual'
    
    print(f"OSC 测试发送工具")
    print(f"目标: {ip}:{port}")
    print(f"模式: {mode}")
    print("-" * 40)
    
    try:
        if mode == 'manual':
            manual_mode(ip, port)
        elif mode == 'circle':
            circle_mode(ip, port)
        elif mode == 'line':
            line_mode(ip, port)
        elif mode == 'random':
            random_mode(ip, port)
        elif mode == 'multi':
            multi_mode(ip, port)
        else:
            print(f"未知模式: {mode}")
            print("可用模式: manual, circle, line, random, multi")
    except KeyboardInterrupt:
        print("\n中断")
    except Exception as e:
        print(f"错误: {e}")

if __name__ == '__main__':
    main()
